using System;
using System.IO;
using System.Net;
using System.Text;
using HSMClient.Configuration;

namespace HSMClient.Connections
{
    public class HttpClient
    {
        private string _address;
        private string _basicAuthToken;

        public HttpClient(string address)
        {
            _address = address;
            string login = ConfigProvider.Instance.ConnectionInfo.UserInfo.Login;
            string password = ConfigProvider.Instance.ConnectionInfo.UserInfo.Password;
            _basicAuthToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{login}:{password}"));
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Ssl3;
        }

        public string Get(string url)
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(new Uri(_address + url).AbsoluteUri);
            request.Timeout = 2000;
            request.PreAuthenticate = true;
            request.Headers.Add("Authorization", "Basic " + _basicAuthToken);
            request.ClientCertificates.Clear();
            request.ClientCertificates.Add(ConfigProvider.Instance.ConnectionInfo.ClientCertificate);

            try
            {
                string responseStr;
                var response =  request.GetResponse();
                using (StreamReader sr = new StreamReader(response.GetResponseStream()))
                {
                    responseStr = sr.ReadToEnd();
                }

                return responseStr;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}