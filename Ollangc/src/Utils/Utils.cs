using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Security.Cryptography;
using System.Linq;

namespace Ollang.Utils
{
    public static class Sys
    {
        private static readonly HttpClient client = new HttpClient();

        public static string Get(string url) => client.GetStringAsync(url).GetAwaiter().GetResult();

        public static string Post(string url, string data) => client.PostAsync(url, new StringContent(data)).GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();

        public static string HttpFetch(string url, string method, Dictionary<string, string>? headers = null, string? body = null)
        {
            var request = new HttpRequestMessage(new HttpMethod(method.ToUpper()), url);
            if (headers != null) foreach (var kvp in headers) request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8);

            var response = client.SendAsync(request).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        public static string B64E(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        public static string B64D(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));

        public static string HexE(string value) => string.Concat(Encoding.UTF8.GetBytes(value).Select(b => b.ToString("x2")));
        public static string HexD(string hex) => Encoding.UTF8.GetString(Enumerable.Range(0, hex.Length / 2).Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16)).ToArray());

        public static string HashSHA256(string value)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
        }

        public static string HashMD5(string value)
        {
            using var md5 = MD5.Create();
            return string.Concat(md5.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
        }

        public static string UUID() => Guid.NewGuid().ToString();

        public static long Timestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public static string? Env(string name) => Environment.GetEnvironmentVariable(name);
        public static void SetEnv(string name, string? value) => Environment.SetEnvironmentVariable(name, value);

        public static string WorkingDir() => Environment.CurrentDirectory;

        public static string[] ListFiles(string path, string pattern = "*") => Directory.GetFiles(path, pattern);
        public static string[] ListDirs(string path, string pattern = "*") => Directory.GetDirectories(path, pattern);

        public static string PathJoin(string a, string b) => Path.Combine(a, b);
        public static string? PathDir(string path) => Path.GetDirectoryName(path);
        public static string PathBase(string path) => Path.GetFileName(path);
        public static string PathExt(string path) => Path.GetExtension(path);
        
        public static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
        public static long MemoryUsage() => System.GC.GetTotalMemory(false);
    }
}
