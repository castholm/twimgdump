using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace TwimgDump
{
    public sealed class MediaDownloader : IDisposable
    {
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;

        public MediaDownloader()
        {
            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression
                    = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,
                UseCookies = false,
            };

            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: false)
            {
                DefaultRequestHeaders =
                {
                    { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8" },
                    //{ "Accept-Encoding", "gzip, deflate, br" }, // Added automatically by the HTTP client.
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Upgrade-Insecure-Requests", "1" },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                },
            };
        }

        public async Task DownloadAsync(Uri uri, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));

            var request = new HttpRequestMessage(HttpMethod.Get, uri);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            Debug.Assert(response.IsSuccessStatusCode);

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(file);

            await contentStream.CopyToAsync(fileStream);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}
