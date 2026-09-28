//! A minimal HTTP/1.1 GET client over a Unix-domain socket — just enough for the Docker Engine API.
//!
//! Why not the `curl` crate (initiative §4.3 has the full paragraph): the Engine API socket is
//! local plaintext HTTP, so libcurl would add nothing but `curl-sys`, whose build silently falls
//! back to compiling its bundled libcurl when pkg-config does not find the system one — exactly the
//! "two libcurls in one process" failure this probe must not have — plus `libz-sys`/`openssl-sys`
//! link lines that would widen the package's `Depends`. This client has no dependencies, speaks
//! only `GET`, and every byte of its framing is unit-tested against captured daemon responses.
//!
//! Scope: one request per connection (`Connection: close`); bodies framed by `Content-Length`, by
//! `Transfer-Encoding: chunked`, or by the peer closing. Every call is bounded by one deadline for
//! connect + write + read, and the body by a size cap.

use std::fmt;
use std::io::{self, Read, Write};
use std::path::Path;
use std::time::{Duration, Instant};

/// A parsed response: status code and the de-framed body.
#[derive(Debug)]
pub struct Response {
    pub status: u16,
    pub body: Vec<u8>,
}

#[derive(Debug)]
pub enum HttpError {
    /// The socket could not be reached or the exchange failed at the I/O level.
    Io(io::Error),
    /// The deadline passed before the response was complete.
    Timeout,
    /// The response is larger than the configured cap.
    TooLarge,
    /// The peer sent something that is not a well-formed HTTP/1.x response.
    Malformed(&'static str),
}

impl fmt::Display for HttpError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            HttpError::Io(error) => write!(f, "{error}"),
            HttpError::Timeout => write!(f, "timed out"),
            HttpError::TooLarge => write!(f, "response exceeds the size cap"),
            HttpError::Malformed(what) => write!(f, "malformed HTTP response: {what}"),
        }
    }
}

impl std::error::Error for HttpError {}

/// Issue `GET <path>` over the Unix socket at `socket` and return the parsed response.
///
/// `path` is sent verbatim as the request target; the caller builds it (see `engine::Endpoint`),
/// which is what keeps the client read-only by construction — there is no other method.
#[cfg(unix)]
pub fn get(
    socket: &Path,
    path: &str,
    timeout: Duration,
    max_bytes: usize,
) -> Result<Response, HttpError> {
    use std::os::unix::net::UnixStream;

    let deadline = Instant::now() + timeout;
    let mut stream = UnixStream::connect(socket).map_err(HttpError::Io)?;
    stream
        .set_write_timeout(Some(timeout))
        .map_err(HttpError::Io)?;
    stream
        .write_all(request(path).as_bytes())
        .map_err(|error| {
            if is_timeout(&error) {
                HttpError::Timeout
            } else {
                HttpError::Io(error)
            }
        })?;
    let raw = read_bounded(&mut stream, deadline, max_bytes, |stream, remaining| {
        stream.set_read_timeout(Some(remaining))
    })?;
    parse_response(&raw, max_bytes)
}

#[cfg(not(unix))]
pub fn get(
    _socket: &Path,
    _path: &str,
    _timeout: Duration,
    _max_bytes: usize,
) -> Result<Response, HttpError> {
    Err(HttpError::Io(io::Error::new(
        io::ErrorKind::Unsupported,
        "Unix-domain sockets are not available on this platform",
    )))
}

/// The request bytes. `Host` is required by HTTP/1.1; the daemon ignores its value.
fn request(path: &str) -> String {
    format!(
        "GET {path} HTTP/1.1\r\nHost: docker\r\nUser-Agent: hsm-linux-probe/{}\r\n\
         Accept: application/json\r\nConnection: close\r\n\r\n",
        env!("CARGO_PKG_VERSION")
    )
}

/// Read until the peer closes, the deadline passes or the cap is hit. `arm` re-arms the socket's
/// per-read timeout with what is left of the overall deadline, so a peer that drips bytes cannot
/// stretch one call past it.
fn read_bounded<R: Read>(
    reader: &mut R,
    deadline: Instant,
    max_bytes: usize,
    mut arm: impl FnMut(&mut R, Duration) -> io::Result<()>,
) -> Result<Vec<u8>, HttpError> {
    // Headers are small next to the cap; allow for them on top of the body limit.
    let limit = max_bytes.saturating_add(64 * 1024);
    let mut raw = Vec::with_capacity(16 * 1024);
    let mut chunk = [0u8; 16 * 1024];
    loop {
        let remaining = deadline.saturating_duration_since(Instant::now());
        if remaining.is_zero() {
            return Err(HttpError::Timeout);
        }
        arm(reader, remaining).map_err(HttpError::Io)?;
        match reader.read(&mut chunk) {
            Ok(0) => return Ok(raw),
            Ok(read) => {
                if raw.len() + read > limit {
                    return Err(HttpError::TooLarge);
                }
                raw.extend_from_slice(&chunk[..read]);
            }
            Err(error) if error.kind() == io::ErrorKind::Interrupted => {}
            Err(error) if is_timeout(&error) => return Err(HttpError::Timeout),
            Err(error) => return Err(HttpError::Io(error)),
        }
    }
}

fn is_timeout(error: &io::Error) -> bool {
    matches!(
        error.kind(),
        io::ErrorKind::WouldBlock | io::ErrorKind::TimedOut
    )
}

/// Parse a complete HTTP/1.x response (the whole connection's bytes).
pub fn parse_response(raw: &[u8], max_bytes: usize) -> Result<Response, HttpError> {
    let header_end = find(raw, b"\r\n\r\n").ok_or(HttpError::Malformed("no end of headers"))?;
    let head = std::str::from_utf8(&raw[..header_end])
        .map_err(|_| HttpError::Malformed("headers are not UTF-8"))?;
    let rest = &raw[header_end + 4..];

    let mut lines = head.split("\r\n");
    let status_line = lines.next().ok_or(HttpError::Malformed("no status line"))?;
    let status = parse_status_line(status_line)?;

    let mut content_length: Option<usize> = None;
    let mut chunked = false;
    for line in lines {
        let (name, value) = line
            .split_once(':')
            .ok_or(HttpError::Malformed("header without a colon"))?;
        let value = value.trim();
        if name.eq_ignore_ascii_case("content-length") {
            let length = value
                .parse::<usize>()
                .map_err(|_| HttpError::Malformed("bad Content-Length"))?;
            content_length = Some(length);
        } else if name.eq_ignore_ascii_case("transfer-encoding") {
            // The last coding is what frames the message (RFC 9112 §6.1).
            chunked = value
                .rsplit(',')
                .next()
                .is_some_and(|coding| coding.trim().eq_ignore_ascii_case("chunked"));
        }
    }

    // Chunked wins over Content-Length when both are present (RFC 9112 §6.3).
    let body = if chunked {
        decode_chunked(rest, max_bytes)?
    } else if let Some(length) = content_length {
        if length > max_bytes {
            return Err(HttpError::TooLarge);
        }
        if rest.len() < length {
            return Err(HttpError::Malformed("body shorter than Content-Length"));
        }
        rest[..length].to_vec()
    } else {
        if rest.len() > max_bytes {
            return Err(HttpError::TooLarge);
        }
        rest.to_vec()
    };

    Ok(Response { status, body })
}

fn parse_status_line(line: &str) -> Result<u16, HttpError> {
    let mut parts = line.splitn(3, ' ');
    let version = parts.next().unwrap_or_default();
    if !version.starts_with("HTTP/1.") {
        return Err(HttpError::Malformed("not an HTTP/1.x status line"));
    }
    let code = parts
        .next()
        .ok_or(HttpError::Malformed("status line without a code"))?;
    if code.len() != 3 {
        return Err(HttpError::Malformed("status code is not three digits"));
    }
    code.parse::<u16>()
        .map_err(|_| HttpError::Malformed("status code is not a number"))
}

/// De-frame a chunked body: `<hex size>[;ext]\r\n<data>\r\n` … `0\r\n[trailers]\r\n`.
fn decode_chunked(mut input: &[u8], max_bytes: usize) -> Result<Vec<u8>, HttpError> {
    let mut body = Vec::new();
    loop {
        let line_end = find(input, b"\r\n").ok_or(HttpError::Malformed("chunk size line"))?;
        let size_line = std::str::from_utf8(&input[..line_end])
            .map_err(|_| HttpError::Malformed("chunk size is not UTF-8"))?;
        let size_text = size_line.split(';').next().unwrap_or_default().trim();
        let size = usize::from_str_radix(size_text, 16)
            .map_err(|_| HttpError::Malformed("chunk size is not hex"))?;
        input = &input[line_end + 2..];
        if size == 0 {
            // Trailers (none from the daemon) end with an empty line; nothing after matters.
            return Ok(body);
        }
        if body.len().saturating_add(size) > max_bytes {
            return Err(HttpError::TooLarge);
        }
        if input.len() < size + 2 {
            return Err(HttpError::Malformed("chunk shorter than its size"));
        }
        body.extend_from_slice(&input[..size]);
        if &input[size..size + 2] != b"\r\n" {
            return Err(HttpError::Malformed("chunk not terminated by CRLF"));
        }
        input = &input[size + 2..];
    }
}

fn find(haystack: &[u8], needle: &[u8]) -> Option<usize> {
    haystack
        .windows(needle.len())
        .position(|window| window == needle)
}

#[cfg(test)]
mod tests {
    use super::*;

    const CAP: usize = 1024 * 1024;

    // Raw bytes read off garage-server's /var/run/docker.sock (Docker 26.1.5, API 1.45) with a
    // plain socket client, headers and framing untouched.
    const CHUNKED: &[u8] = include_bytes!("../../../fixtures/docker/http/inspect-chunked.http");
    const CONTENT_LENGTH: &[u8] =
        include_bytes!("../../../fixtures/docker/http/stats-content-length.http");
    const NOT_FOUND: &[u8] = include_bytes!("../../../fixtures/docker/http/missing-404.http");

    #[test]
    fn a_real_chunked_daemon_response_is_deframed() {
        let response = parse_response(CHUNKED, CAP).expect("parse");
        assert_eq!(response.status, 200);
        let json: serde_json::Value = serde_json::from_slice(&response.body).expect("json body");
        assert_eq!(json["Name"], "/portainer-portainer-1");
    }

    #[test]
    fn a_real_content_length_daemon_response_is_deframed() {
        let response = parse_response(CONTENT_LENGTH, CAP).expect("parse");
        assert_eq!(response.status, 200);
        assert_eq!(response.body.len(), 1729);
        let json: serde_json::Value = serde_json::from_slice(&response.body).expect("json body");
        assert!(json["cpu_stats"]["system_cpu_usage"].is_u64());
    }

    #[test]
    fn a_real_404_keeps_its_status_and_message() {
        let response = parse_response(NOT_FOUND, CAP).expect("parse");
        assert_eq!(response.status, 404);
        assert_eq!(
            std::str::from_utf8(&response.body).unwrap(),
            "{\"message\":\"No such container: nonexistent\"}\n"
        );
    }

    #[test]
    fn chunk_extensions_and_small_chunks_are_handled() {
        let raw = b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n\
                    3;name=x\r\nabc\r\n1\r\nd\r\n0\r\n\r\n";
        assert_eq!(parse_response(raw, CAP).unwrap().body, b"abcd");
    }

    #[test]
    fn a_close_delimited_body_is_taken_whole() {
        let raw = b"HTTP/1.0 200 OK\r\nContent-Type: application/json\r\n\r\n{}";
        assert_eq!(parse_response(raw, CAP).unwrap().body, b"{}");
    }

    #[test]
    fn truncated_or_malformed_responses_are_errors_not_partial_bodies() {
        // A value read from half a response must never be posted (rule #8).
        for raw in [
            &b"HTTP/1.1 200 OK\r\nContent-Length: 10\r\n\r\nabc"[..],
            b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nab",
            b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\n",
            b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n2\r\nabXX0\r\n\r\n",
            b"HTTP/1.1 200 OK\r\nContent-Length: 2",
            b"SSH-2.0-OpenSSH\r\n\r\n",
            b"HTTP/1.1 2x0 OK\r\n\r\n",
            b"HTTP/1.1 200 OK\r\nno colon here\r\n\r\n",
        ] {
            assert!(
                matches!(parse_response(raw, CAP), Err(HttpError::Malformed(_))),
                "{:?}",
                String::from_utf8_lossy(raw)
            );
        }
    }

    #[test]
    fn oversized_bodies_are_refused_under_every_framing() {
        let big = format!(
            "HTTP/1.1 200 OK\r\nContent-Length: {}\r\n\r\n{}",
            CAP + 1,
            "x".repeat(CAP + 1)
        );
        assert!(matches!(
            parse_response(big.as_bytes(), CAP),
            Err(HttpError::TooLarge)
        ));
        let chunked =
            b"HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\n\r\n8\r\n12345678\r\n0\r\n\r\n";
        assert!(matches!(
            parse_response(chunked, 4),
            Err(HttpError::TooLarge)
        ));
        let close = b"HTTP/1.1 200 OK\r\n\r\n12345678";
        assert!(matches!(parse_response(close, 4), Err(HttpError::TooLarge)));
    }

    #[test]
    fn the_request_is_a_plain_read_only_get() {
        let text = request("/v1.45/containers/json?all=true");
        assert!(text.starts_with("GET /v1.45/containers/json?all=true HTTP/1.1\r\n"));
        assert!(text.contains("\r\nConnection: close\r\n"));
        assert!(text.ends_with("\r\n\r\n"));
    }

    /// A reader that hands out one byte per call and never ends: models a peer that drips data.
    struct Drip;
    impl Read for Drip {
        fn read(&mut self, buf: &mut [u8]) -> io::Result<usize> {
            std::thread::sleep(Duration::from_millis(5));
            buf[0] = b'x';
            Ok(1)
        }
    }

    #[test]
    fn a_dripping_peer_cannot_stretch_a_call_past_the_deadline() {
        let started = Instant::now();
        let result = read_bounded(
            &mut Drip,
            Instant::now() + Duration::from_millis(60),
            CAP,
            |_, _| Ok(()),
        );
        assert!(matches!(result, Err(HttpError::Timeout)));
        assert!(started.elapsed() < Duration::from_secs(2));
    }

    #[test]
    fn a_reader_past_the_cap_is_cut_off() {
        let data = vec![b'x'; 200 * 1024];
        let result = read_bounded(
            &mut &data[..],
            Instant::now() + Duration::from_secs(5),
            64 * 1024,
            |_, _| Ok(()),
        );
        assert!(matches!(result, Err(HttpError::TooLarge)));
    }

    #[cfg(unix)]
    #[test]
    fn a_missing_socket_is_an_io_error() {
        let result = get(
            Path::new("/nonexistent/docker.sock"),
            "/version",
            Duration::from_secs(1),
            CAP,
        );
        assert!(matches!(result, Err(HttpError::Io(_))));
    }

    #[cfg(unix)]
    #[test]
    fn a_round_trip_over_a_real_unix_socket_works() {
        use std::os::unix::net::UnixListener;

        let path = std::env::temp_dir().join(format!("hsm-probe-http-{}.sock", std::process::id()));
        let _ = std::fs::remove_file(&path);
        let listener = UnixListener::bind(&path).expect("bind");
        let server = std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().expect("accept");
            let mut request = Vec::new();
            let mut buffer = [0u8; 1024];
            while find(&request, b"\r\n\r\n").is_none() {
                let read = stream.read(&mut buffer).expect("read");
                request.extend_from_slice(&buffer[..read]);
            }
            stream.write_all(CHUNKED).expect("write");
            String::from_utf8(request).expect("utf8")
        });

        let response = get(
            &path,
            "/v1.45/containers/x/json",
            Duration::from_secs(5),
            CAP,
        )
        .expect("round trip");
        let request = server.join().expect("server");
        let _ = std::fs::remove_file(&path);

        assert_eq!(response.status, 200);
        assert!(request.starts_with("GET /v1.45/containers/x/json HTTP/1.1\r\n"));
    }
}
