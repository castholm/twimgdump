using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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

        public async Task<(IList<(string TweetId, IList<string> MediaUrls)> Tweets, string CursorTop, string CursorBottom)> FetchTweetsAsync(
            string userScreenName,
            string? cursor)
        {
            _guestToken ??= await FetchGuestTokenAsync();
            _csrfToken ??= await FetchCsrfTokenAsync();

            if (!_knownUserIds.TryGetValue(userScreenName, out var userId))
            {
                userId = await FetchUserIdAsync(userScreenName);
                _knownUserIds.Add(userScreenName, userId);
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

            var mediaRequest = new HttpRequestMessage(HttpMethod.Get, requestUri)
            {
                Headers =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "Accept", "*/*" },
                    //{ "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/json" },
                    { "x-csrf-token", _csrfToken! },
                    { "x-guest-token", _guestToken! },
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var mediaResponse = await _httpClient.SendAsync(mediaRequest);
            Debug.Assert(mediaResponse.IsSuccessStatusCode);

            using var mediaJson = await JsonDocument.ParseAsync(await mediaResponse.Content.ReadAsStreamAsync());

            var tweetsObject = mediaJson.RootElement
                .GetProperty("globalObjects")
                .GetProperty("tweets");

            var instructionsArray = mediaJson.RootElement
                .GetProperty("timeline")
                .GetProperty("instructions");

            var entriesArray = instructionsArray.EnumerateArray()
                .SelectMany(x => x.TryGetProperty("addEntries", out var addEntriesObject)
                    ? Enumerable.Repeat(addEntriesObject.GetProperty("entries"), 1)
                    : Enumerable.Empty<JsonElement>())
                .First();

            var json = mediaJson.ToString();

            var tweetObjects = entriesArray.EnumerateArray()
                .SelectMany(x
                    => x.GetProperty("sortIndex").GetString() is string sortIndex
                    && x.GetProperty("content").TryGetProperty("item", out var itemObject)
                    && itemObject.GetProperty("content").TryGetProperty("tweet", out var tweetReferenceObject)
                    && tweetReferenceObject.GetProperty("id").GetString() is string tweetId
                        ? Enumerable.Repeat((
                            TweetObject: tweetsObject.GetProperty(tweetId),
                            SortIndex: sortIndex),
                            1)
                        : Enumerable.Empty<(JsonElement TweetObject, string SortIndex)>())
                .OrderByDescending(x => x.SortIndex, StringComparer.Ordinal)
                .Select(x => x.TweetObject)
                .ToList();

            var mediaTweets = tweetObjects
                .SelectMany(x
                    => x.GetProperty("id_str").GetString() is string tweetId
                    && x.GetProperty("extended_entities").GetProperty("media") is JsonElement mediaArray
                    && mediaArray.EnumerateArray()
                            .Select(mediaObject =>
                            {
                                var type = mediaObject.GetProperty("type").GetString();

                                if (type == "photo")
                                {
                                    return Regex.Replace(
                                        mediaObject.GetProperty("media_url_https").GetString(),
                                        @"^(.+)\.(.+)$",
                                        "$1?format=$2&name=orig");
                                }

                                var variantsArray = mediaObject.GetProperty("video_info").GetProperty("variants");

                                // TODO: Log the media type and resolution.  We currently assume that the variant with
                                // the highest bitrate will be an MP4 file and have the highest resolution.

                                return variantsArray.EnumerateArray()
                                    .OrderByDescending(x => x.TryGetProperty("bitrate", out var bitrateProperty)
                                        ? bitrateProperty.GetInt32()
                                        : 0)
                                    .Select(x => x.GetProperty("url").GetString())
                                    .First();
                            })
                            .ToList()
                            is IList<string> mediaUrls
                        ? Enumerable.Repeat((
                            TweetId: tweetId,
                            MediaUrl: mediaUrls),
                            1)
                        : Enumerable.Empty<(string TweetId, IList<string> MediaUrls)>())
                .ToList();

            var cursorTop = entriesArray.EnumerateArray()
                .SelectMany(x
                    => x.GetProperty("content").TryGetProperty("operation", out var operationObject)
                    && operationObject.TryGetProperty("cursor", out var cursorObject)
                    && cursorObject.GetProperty("cursorType").GetString() == "Top"
                        ? Enumerable.Repeat(cursorObject.GetProperty("value").GetString(), 1)
                        : Enumerable.Empty<string>())
                .Single();

            var cursorBottom = entriesArray.EnumerateArray()
                .SelectMany(x
                    => x.GetProperty("content").TryGetProperty("operation", out var operationObject)
                    && operationObject.TryGetProperty("cursor", out var cursorObject)
                    && cursorObject.GetProperty("cursorType").GetString() == "Bottom"
                        ? Enumerable.Repeat(cursorObject.GetProperty("value").GetString(), 1)
                        : Enumerable.Empty<string>())
                .Single();

            return (mediaTweets, cursorTop, cursorBottom);
        }

        private async Task<string> FetchGuestTokenAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.twitter.com/1.1/guest/activate.json")
            {
                Headers =
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "Accept", "*/*" },
                    //{ "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/x-www-form-urlencoded" },
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            var guestToken = json.RootElement.GetProperty("guest_token").GetString();
            Debug.Assert(guestToken is object);

            _httpClientHandler.CookieContainer.SetCookies(new Uri("https://twitter.com/"), $"gt={guestToken}; Max-Age=10800; Domain=.twitter.com; Path=/; Secure");

            return guestToken;
        }

        private Task<string> FetchCsrfTokenAsync()
        {
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
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:74.0) Gecko/20100101 Firefox/74.0" },
                    { "Accept", "*/*" },
                    //{ "Accept-Encoding", "gzip, deflate, br" },
                    { "Accept-Language", "en-US,en;q=0.5" },
                    { "Connection", "keep-alive" },
                    { "DNT", "1" },
                    { "Origin", "https://twitter.com" },
                    { "Referer", "https://twitter.com/" },
                    { "TE", "Trailers" },
                    { "authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA" },
                    //{ "content-type", "application/json" },
                    { "x-csrf-token", _csrfToken! },
                    { "x-guest-token", _guestToken! },
                    { "x-twitter-client-language", "en" },
                    { "x-twitter-active-user", "yes" },
                },
            };

            var response = await _httpClient.SendAsync(request);
            Debug.Assert(response.IsSuccessStatusCode);

            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            var userId = json.RootElement
                .GetProperty("data")
                .GetProperty("user")
                .GetProperty("rest_id")
                .GetString();
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
