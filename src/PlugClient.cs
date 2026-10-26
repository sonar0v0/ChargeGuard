using System;
using System.Net;

namespace ChargeGuard
{
    public static class PlugClient
    {
        public static bool Send(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 5000;
                req.ReadWriteTimeout = 5000;
                req.UserAgent = "ChargeGuard/1.0";
                req.Proxy = null; // LAN plugs must not go through a system proxy.
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    Log.Write(string.Format("HTTP {0} {1}", code, url));
                    return code >= 200 && code < 400;
                }
            }
            catch (WebException ex)
            {
                HttpWebResponse resp = ex.Response as HttpWebResponse;
                if (resp != null)
                {
                    int code = (int)resp.StatusCode;
                    Log.Write(string.Format("HTTP {0} {1}: {2}", code, url, ex.Message));
                    resp.Close();
                }
                else
                {
                    Log.Write(string.Format("HTTP 请求失败 {0}: {1}", url, ex.Message));
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Write(string.Format("HTTP 请求异常 {0}: {1}", url, ex.Message));
                return false;
            }
        }
    }
}
