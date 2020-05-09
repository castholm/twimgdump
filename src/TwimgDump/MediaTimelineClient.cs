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
        private readonly string _username;

        private string? _guestToken;
        private string? _csrfToken;
        private ulong? _userId;

        public MediaTimelineClient(string username)
        {
            _httpClientHandler = new HttpClientHandler
            {
                AutomaticDecompression
                    = DecompressionMethods.GZip
                    | DecompressionMethods.Deflate
                    | DecompressionMethods.Brotli,
            };
            _httpClient = new HttpClient(_httpClientHandler, disposeHandler: false);
            _username = username;
        }

        public async Task<(IList<TweetMedia> Media, string CursorTop, string CursorBottom)> FetchTweetsAsync(string? cursor)
        {
            _guestToken ??= await FetchGuestTokenAsync();
            _csrfToken ??= await FetchCsrfTokenAsync();
            _userId ??= await FetchUserIdAsync();

            var requestUri = $"https://api.twitter.com/2/timeline/media/{_userId}.json"
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

            var entriesArray = instructionsArray.EnumerateElements()
                .Select(x => x["addEntries"]["entries"])
                .Where(x => x.Type == JsonNodeType.Array)
                .FirstOrDefault();
            Debug.Assert(entriesArray.Type == JsonNodeType.Array);

            var tweetObjects = entriesArray.EnumerateElements()
                .OrderByDescending(x => x["sortIndex"].GetString(), StringComparer.Ordinal)
                .Select(x => tweetsObject[x["content"]["item"]["content"]["tweet"]["id"].GetString()])
                .Where(x => x.Type == JsonNodeType.Object)
                .ToList();

            var media = tweetObjects
                .SelectMany(tweetObject => ToTweetMedia(tweetObject))
                .ToList();

            var cursorTop = entriesArray.EnumerateElements()
                .Select(x => x["content"]["operation"]["cursor"])
                .Where(x => x["cursorType"].GetString() == "Top")
                .Select(x => x["value"].GetString())
                .OfType<string>()
                .First();

            var cursorBottom = entriesArray.EnumerateElements()
                .Select(x => x["content"]["operation"]["cursor"])
                .Where(x => x["cursorType"].GetString() == "Bottom")
                .Select(x => x["value"].GetString())
                .OfType<string>()
                .First();

            return (media, cursorTop, cursorBottom);
        }

        private IEnumerable<TweetMedia> ToTweetMedia(JsonNode tweetObject)
        {
            var tweetId = ulong.Parse(
                tweetObject["id_str"].GetString()!,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);

            var created = DateTime.ParseExact(
                    tweetObject["created_at"].GetString()!,
                    "ddd MMM dd HH':'mm':'ss zz'00' yyyy",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None);

            var mediaObjects = tweetObject["extended_entities"]["media"].EnumerateElements().ToList();

            var currentIndex = 1;
            foreach (var mediaObject in mediaObjects)
            {
                var mediaId = ulong.Parse(
                    mediaObject["id_str"].GetString()!,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture);

                var type = mediaObject["type"].GetString()!.ToUpperInvariant() switch
                {
                    "PHOTO" => TweetMediaType.Photo,
                    "VIDEO" => TweetMediaType.Video,
                    "ANIMATED_GIF" => TweetMediaType.AnimatedGif,
                    _ => throw new InvalidOperationException("Unknown media type."),
                };

                Uri urlRaw;
                Uri urlFormatted;
                if (type == TweetMediaType.Photo)
                {
                    urlRaw = new Uri(mediaObject["media_url_https"].GetString()!);

                    urlFormatted = new Uri(Regex.Replace(urlRaw.AbsoluteUri, @"^(.+)\.(.+)$", "$1?format=$2&name=orig"));
                }
                else
                {
                    var variantsArray = mediaObject["video_info"]["variants"];
                    Debug.Assert(variantsArray.Type == JsonNodeType.Array);

                    // We currently assume that the variant with the highest bitrate will be an MP4 file and
                    // have the highest resolution.

                    urlRaw = variantsArray.EnumerateElements()
                        .OrderByDescending(x => x["bitrate"].GetInt32())
                        .Select(x => new Uri(x["url"].GetString()!))
                        .First();

                    urlFormatted = urlRaw;
                }

                var filename = urlRaw.Segments.Last();

                var stem = Path.GetFileNameWithoutExtension(filename);
                var extension = Path.GetExtension(filename);

                var width = mediaObject["original_info"]["width"].GetInt32()!.Value;
                var height = mediaObject["original_info"]["height"].GetInt32()!.Value;

                yield return new TweetMedia(
                    urlFormatted,
                    type,
                    _userId!.Value,
                    _username,
                    tweetId,
                    created,
                    mediaId,
                    stem,
                    extension,
                    currentIndex,
                    mediaObjects.Count,
                    width,
                    height);

                currentIndex += 1;
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

            var guestToken = rootNode["guest_token"].GetString()!;

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

        private async Task<ulong> FetchUserIdAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.twitter.com/graphql/P8ph10GzBbdMqWZxulqCfA/UserByScreenName?variables=%7B%22screen_name%22%3A%22{_username}%22%2C%22withHighlightedLabel%22%3Afalse%7D")
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

            var userId = ulong.Parse(
                rootNode["data"]["user"]["rest_id"].GetString()!,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture);

            return userId;
        }

        public void Dispose()
        {
            _httpClient.Dispose();
            _httpClientHandler.Dispose();
        }
    }
}
