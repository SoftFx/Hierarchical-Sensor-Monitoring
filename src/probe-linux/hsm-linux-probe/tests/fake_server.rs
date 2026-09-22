//! Fake-server capture test (initiative §6, "Integration").
//!
//! Drives the real collector through its real libcurl transport against a loopback listener and
//! asserts the two properties this slice owns:
//!
//! 1. requests arrive carrying the `Key` header, so the probe's transport wiring is live;
//! 2. the key value never appears in anything the probe logs.
//!
//! Plaintext over `127.0.0.1` is the pattern the native collector's own HTTP E2E test uses
//! (`src/native/collector/tests/http_capture_server.hpp`): an explicit `http://` scheme plus
//! `allow_plaintext_transport`. A TLS listener would require a test CA and buy nothing here —
//! certificate verification is libcurl's, exercised by the collector's own lanes.
//!
//! Linux-only: the whole probe is.

#![cfg(target_os = "linux")]

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use hsm_collector::{Collector, CollectorOptions, SensorOptions};

const ACCESS_KEY: &str = "probe-integration-access-key-4f1c";

#[derive(Clone, Debug)]
struct CapturedRequest {
    method: String,
    path: String,
    headers: String,
    body: String,
}

struct CaptureServer {
    port: u16,
    requests: Arc<Mutex<Vec<CapturedRequest>>>,
    stop: Arc<AtomicBool>,
    worker: Option<std::thread::JoinHandle<()>>,
}

impl CaptureServer {
    fn start() -> Self {
        let listener = TcpListener::bind("127.0.0.1:0").expect("bind loopback");
        let port = listener.local_addr().expect("local addr").port();
        // A short accept timeout lets the worker notice the stop flag even with no client.
        listener
            .set_nonblocking(true)
            .expect("non-blocking listener");

        let requests = Arc::new(Mutex::new(Vec::new()));
        let stop = Arc::new(AtomicBool::new(false));

        let worker_requests = Arc::clone(&requests);
        let worker_stop = Arc::clone(&stop);
        let worker = std::thread::spawn(move || {
            while !worker_stop.load(Ordering::SeqCst) {
                match listener.accept() {
                    Ok((stream, _)) => {
                        if let Some(request) = serve(stream) {
                            worker_requests.lock().expect("requests lock").push(request);
                        }
                    }
                    Err(ref error) if error.kind() == std::io::ErrorKind::WouldBlock => {
                        std::thread::sleep(Duration::from_millis(10));
                    }
                    Err(_) => break,
                }
            }
        });

        Self {
            port,
            requests,
            stop,
            worker: Some(worker),
        }
    }

    fn requests(&self) -> Vec<CapturedRequest> {
        self.requests.lock().expect("requests lock").clone()
    }

    /// Wait until at least one request with the given path prefix has been captured.
    fn wait_for(&self, path: &str, timeout: Duration) -> Option<CapturedRequest> {
        let deadline = Instant::now() + timeout;
        while Instant::now() < deadline {
            if let Some(request) = self
                .requests()
                .into_iter()
                .find(|request| request.path.contains(path))
            {
                return Some(request);
            }
            std::thread::sleep(Duration::from_millis(20));
        }
        None
    }
}

impl Drop for CaptureServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::SeqCst);
        if let Some(worker) = self.worker.take() {
            let _ = worker.join();
        }
    }
}

fn serve(mut stream: TcpStream) -> Option<CapturedRequest> {
    stream.set_read_timeout(Some(Duration::from_secs(5))).ok()?;
    // The collector may reuse a connection, but one request per connection is enough for this
    // assertion and keeps the parser trivial.
    stream.set_nodelay(true).ok();

    let mut raw = Vec::new();
    let mut buffer = [0u8; 4096];
    let mut header_end = None;
    let mut content_length = 0usize;

    loop {
        match stream.read(&mut buffer) {
            Ok(0) => break,
            Ok(read) => {
                raw.extend_from_slice(&buffer[..read]);
                if header_end.is_none() {
                    if let Some(index) = find(&raw, b"\r\n\r\n") {
                        header_end = Some(index);
                        let head = String::from_utf8_lossy(&raw[..index]).to_string();
                        content_length = header_value(&head, "content-length")
                            .and_then(|value| value.parse().ok())
                            .unwrap_or(0);
                    }
                }
                if let Some(index) = header_end {
                    if raw.len() - (index + 4) >= content_length {
                        break;
                    }
                }
            }
            Err(_) => break,
        }
    }

    let _ = stream.write_all(b"HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
    let _ = stream.flush();

    let index = header_end?;
    let head = String::from_utf8_lossy(&raw[..index]).to_string();
    let mut lines = head.split("\r\n");
    let request_line = lines.next()?;
    let mut parts = request_line.split(' ');
    let method = parts.next()?.to_string();
    let path = parts.next()?.to_string();
    let headers = head
        .split_once("\r\n")
        .map(|(_, rest)| rest.to_string())
        .unwrap_or_default();
    let body = String::from_utf8_lossy(&raw[index + 4..]).to_string();

    Some(CapturedRequest {
        method,
        path,
        headers,
        body,
    })
}

fn find(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

fn header_value(headers: &str, name_lower: &str) -> Option<String> {
    headers.split("\r\n").find_map(|line| {
        let (name, value) = line.split_once(':')?;
        (name.trim().to_ascii_lowercase() == name_lower).then(|| value.trim().to_string())
    })
}

#[test]
fn the_transport_sends_the_key_header_and_never_logs_the_key() {
    let server = CaptureServer::start();

    let logs: Arc<Mutex<Vec<String>>> = Arc::new(Mutex::new(Vec::new()));

    {
        let mut options = CollectorOptions::new(ACCESS_KEY, "http://127.0.0.1", server.port);
        options.allow_plaintext_transport = true;
        options.module = Some("LinuxProbeTest".into());
        options.computer_name = Some("test-host".into());
        options.package_collect_period = Some(Duration::from_millis(50));
        options.request_timeout = Some(Duration::from_secs(5));

        let collector = Collector::new(&options).expect("create collector");

        let sink = Arc::clone(&logs);
        collector
            .set_logger(move |level, message| {
                sink.lock()
                    .expect("log lock")
                    .push(format!("{}|{message}", level.as_str()));
            })
            .expect("set logger");

        collector
            .use_http_transport()
            .expect("install the libcurl transport");

        let load = collector
            .double_sensor(
                "CPU/Load average 1m",
                &SensorOptions::default().with_ttl(Duration::from_secs(180)),
            )
            .expect("create the load-average sensor");

        collector.start().expect("start");
        load.add(0.42).expect("post a load average");

        // Registration goes to /commands at Start; values batch to /list.
        let registration = server
            .wait_for("/commands", Duration::from_secs(20))
            .expect("the collector must register its sensors on start");
        assert_eq!(registration.method, "POST");
        assert!(
            header_value(&registration.headers, "key").as_deref() == Some(ACCESS_KEY),
            "the registration request must carry the Key header: {}",
            registration.headers
        );

        let data = server
            .wait_for("/list", Duration::from_secs(20))
            .expect("the posted value must reach the server");
        assert_eq!(data.method, "POST");
        assert_eq!(
            header_value(&data.headers, "key").as_deref(),
            Some(ACCESS_KEY)
        );
        assert!(
            data.body.contains("CPU/Load average 1m"),
            "unexpected body: {}",
            data.body
        );

        collector.stop().expect("stop");
    }

    let captured_logs = logs.lock().expect("log lock").clone();
    assert!(
        !captured_logs.is_empty(),
        "the collector must have logged something"
    );
    for line in &captured_logs {
        assert!(
            !line.contains(ACCESS_KEY),
            "the access key leaked into a log line: {line}"
        );
    }

    // The key travels in the header, and nowhere else: not in the URL, not in the payload.
    for request in server.requests() {
        assert!(
            !request.path.contains(ACCESS_KEY),
            "the key leaked into a URL: {}",
            request.path
        );
        assert!(
            !request.body.contains(ACCESS_KEY),
            "the key leaked into a body: {}",
            request.body
        );
    }
}
