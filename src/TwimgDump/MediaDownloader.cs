using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace TwimgDump
{
    public sealed class MediaDownloader : IDisposable
    {
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;

        public MediaDownloader(string downloadDirectory)
        {
            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression
                    = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,
                UseCookies = false,
            };
            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: false);
            _downloadDirectory = downloadDirectory;

            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
            }
        }

        public async Task DownloadAsync(string tweetId, string mediaUrl)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, mediaUrl)
            {
                Headers =
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

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            var mediaType = response.Content.Headers.ContentType.MediaType;
            var extension
                = mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                ? ".png"
                : mediaType.Equals("video/mp4", StringComparison.OrdinalIgnoreCase)
                ? ".mp4"
                : null;

            var originalFilename = request.RequestUri.Segments.Last();
            var newFilename = Path.ChangeExtension($"{tweetId}_{originalFilename}", extension);

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = File.Create(Path.Join(_downloadDirectory, newFilename));

            await contentStream.CopyToAsync(fileStream);
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}
