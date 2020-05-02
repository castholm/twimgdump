using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TwimgDump.Json;

namespace TwimgDump
{
    public sealed class MediaTimelineClient : IDisposable
    {
        private readonly HttpClientHandler _httpClientHandler;
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, string> _knownUserIds;

        private string? _guestToken;
        private string? _csrfToken;

        public MediaTimelineClient()
        {
            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression
                    = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,
            };
            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: false);
            _knownUserIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public async Task<(IList<TweetMedia> Media, string CursorTop, string CursorBottom)> FetchTweetsAsync(
            string username,
            string? cursor)
        {
            _guestToken ??= await FetchGuestTokenAsync();
            _csrfToken ??= await FetchCsrfTokenAsync();

            if (!_knownUserIds.TryGetValue(username, out var userId))
            {
                userId = await FetchUserIdAsync(username);
                _knownUserIds.Add(username, userId);
            }

            var requestUri = $"https://api.twitter.com/2/timeline/media/{userId}.json"
                + "?include_profile_interstitial_type=1"
                + "&include_blocking=1"
                + "&include_blocked_by=1"
                + "&include_followed_by=1"
                + "&include_want_retweets=1"
                + "&include_mute_edge=1"
                + "&include_can_dm=1"
                + "&include_can_media_tag=1"
                + "&skip_status=1"
                + "&cards_platform=Web-12"
                + "&include_cards=1"
                + "&include_composer_source=true"
                + "&include_ext_alt_text=true"
                + "&include_reply_count=1"
                + "&tweet_mode=extended"
                + "&include_entities=true"
                + "&include_user_entities=true"
                + "&include_ext_media_color=true"
                + "&include_ext_media_availability=true"
                + "&send_error_codes=true"
                + "&simple_quoted_tweets=true"
                + "&count=20"
                + (cursor is object ? $"&cursor={WebUtility.UrlEncode(cursor)}" : "")
                + "&ext=mediaStats%2CcameraMoment";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUri)
            {
                Headers =
                {
                    { "Accept", "*/*" },
                    { "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/json" }, // Sent by browsers but not supported by .NET in this context.
                    { "x-csrf-token", _csrfToken! },
                    { "x-guest-token", _guestToken! },
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            // Twitter's timeline API response consists of two top level objects:
            //
            // o  'globalObjects', which is a collection of tweets, users and other data.
            //
            // o  'timeline', which contains instructions to the client letting it know what tweets and other elements
            //    to add to the timeline in what order.  These instructions also include two cursors pointing to the
            //    previous and next page of tweets.

            using var jsonDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var rootNode = jsonDocument.RootElement.AsJsonNode();

            var tweetsObject = rootNode["globalObjects"]["tweets"];
            Debug.Assert(tweetsObject.Type == JsonNodeType.Object);

            var instructionsArray = rootNode["timeline"]["instructions"];
            Debug.Assert(instructionsArray.Type == JsonNodeType.Array);

            var entriesArray = instructionsArray.Elements
                .Select(x => x["addEntries"]["entries"])
                .Where(x => x.Type == JsonNodeType.Array)
                .FirstOrDefault();
            Debug.Assert(entriesArray.Type == JsonNodeType.Array);

            var tweetObjects = entriesArray.Elements
                .OrderByDescending(x => x["sortIndex"].GetString(), StringComparer.Ordinal)
                .Select(x => tweetsObject[x["content"]["item"]["content"]["tweet"]["id"].GetString()])
                .Where(x => x.Type == JsonNodeType.Object)
                .ToList();

            var media = tweetObjects
                .SelectMany(tweetObject => ToTweetMedia(userId, username, tweetObject))
                .ToList();

            var cursorTop = entriesArray.Elements
                .Select(x => x["content"]["operation"]["cursor"])
                .Where(x => x["cursorType"].GetString() == "Top")
                .Select(x => x["value"].GetString())
                .OfType<string>()
                .FirstOrDefault();
            Debug.Assert(cursorTop is object);

            var cursorBottom = entriesArray.Elements
                .Select(x => x["content"]["operation"]["cursor"])
                .Where(x => x["cursorType"].GetString() == "Bottom")
                .Select(x => x["value"].GetString())
                .OfType<string>()
                .FirstOrDefault();
            Debug.Assert(cursorBottom is object);

            return (media, cursorTop, cursorBottom);
        }

        private static IEnumerable<TweetMedia> ToTweetMedia(
            string userId,
            string username,
            JsonNode tweetObject)
        {
            var tweetId = tweetObject["id_str"].GetString();
            Debug.Assert(tweetId is object);

            var createdRaw = tweetObject["created_at"].GetString();
            Debug.Assert(createdRaw is object);

            var created = DateTime.ParseExact(
                    createdRaw,
                    "ddd MMM dd HH':'mm':'ss zz'00' yyyy",
                    CultureInfo.InvariantCulture)
                .ToUniversalTime()
                .ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

            var mediaObjects = tweetObject["extended_entities"]["media"].Elements.ToList();

            var index = 1;
            foreach (var mediaObject in mediaObjects)
            {
                var mediaId = mediaObject["id_str"].GetString();
                Debug.Assert(mediaId is object);

                var type = mediaObject["type"].GetString();
                Debug.Assert(type is object);

                string url;
                string baseName;
                string extension;
                if (type == "photo")
                {
                    var urlRaw = mediaObject["media_url_https"].GetString();
                    Debug.Assert(urlRaw is object);

                    url = Regex.Replace(urlRaw, @"^(.+)\.(.+)$", "$1?format=$2&name=orig");

                    var filename = new Uri(urlRaw).Segments.Last();

                    baseName = Path.GetFileNameWithoutExtension(filename);
                    extension = Path.GetExtension(filename).TrimStart('.');
                }
                else
                {
                    var variantsArray = mediaObject["video_info"]["variants"];
                    Debug.Assert(variantsArray.Type == JsonNodeType.Array);

                    // We currently assume that the variant with the highest bitrate will be an MP4 file and
                    // have the highest resolution.

                    var urlRaw = variantsArray.Elements
                        .OrderByDescending(x => x["bitrate"].GetInt32())
                        .Select(x => x["url"].GetString())
                        .FirstOrDefault();
                    Debug.Assert(urlRaw is object);

                    url = urlRaw;

                    var filename = new Uri(urlRaw).Segments.Last();

                    baseName = Path.GetFileNameWithoutExtension(filename);
                    extension = Path.GetExtension(filename).TrimStart('.');
                }

                var width = mediaObject["original_info"]["width"].GetInt32();
                Debug.Assert(width is object);

                var height = mediaObject["original_info"]["height"].GetInt32();
                Debug.Assert(height is object);

                yield return new TweetMedia
                {
                    Url = url,
                    UserId = userId,
                    Username = username,
                    TweetId = tweetId,
                    Created = created,
                    Count = mediaObjects.Count,
                    MediaId = mediaId,
                    Index = index,
                    BaseName = baseName,
                    Extension = extension,
                    Width = width.Value,
                    Height = height.Value,
                };

                index += 1;
            }
        }

        private async Task<string> FetchGuestTokenAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twitter.com/1.1/guest/activate.json")
            {
                Headers =
                {
                    { "Accept", "*/*" },
                    { "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/json" }, // Sent by browsers but not supported by .NET in this context.
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            using var jsonDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var rootNode = jsonDocument.RootElement.AsJsonNode();

            var guestToken = rootNode["guest_token"].GetString();
            Debug.Assert(guestToken is object);

            _httpClientHandler.CookieContainer.SetCookies(new Uri("https://twitter.com/"), $"gt={guestToken}; Max-Age=10800; Domain=.twitter.com; Path=/; Secure");

            return guestToken;
        }

        private Task<string> FetchCsrfTokenAsync()
        {
            // Twitter uses the double submit cookie pattern to mitigate CSRF.  The CSRF token is generated
            // client-side and is a 32-digit hexadecimal sequence, which conveniently happens to be the length of a
            // GUID formatted according to the 'N' format specifier.

            var csrfToken = Guid.NewGuid().ToString("N");

            _httpClientHandler.CookieContainer.SetCookies(new Uri("https://twitter.com/"), $"ct0={csrfToken}; Max-Age=21600; Domain=.twitter.com; Path=/; Secure");

            return Task.FromResult(csrfToken);
        }

        private async Task<string> FetchUserIdAsync(string userScreenName)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitter.com/graphql/P8ph10GzBbdMqWZxulqCfA/UserByScreenName?variables=%7B%22screen_name%22%3A%22{userScreenName}%22%2C%22withHighlightedLabel%22%3Afalse%7D")
            {
                Headers =
                {
                    { "Accept", "*/*" },
                    { "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/json" }, // Sent by browsers but not supported by .NET in this context.
                    { "x-csrf-token", _csrfToken! },
                    { "x-guest-token", _guestToken! },
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            using var jsonDocument = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var rootNode = jsonDocument.RootElement.AsJsonNode();

            var userId = rootNode["data"]["user"]["rest_id"].GetString();
            Debug.Assert(userId is object);

            return userId;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}
