# twimgdump

Just because you've liked or retweeted a piece of media on twitter doesn't mean that it will still be there tomorrow.
The author might suddenly become permanently suspended.  Or they might have a nervous breakdown and permanently delete
a bunch of their old tweets.  Or they might be "canceled" for Americentric ideological reasons and be forced to delete
tweets that the public deems inappropriate.  Whatever the reason may be, the point remains: if you like a user's
uploaded media, you probably want to keep local copies of them.

twimgdump is a small command-line tool I developed to help me make local copies of art uploaded by artists I follow on
twitter.  It fetches all tweets posted to a Twitter user's media timeline and saves the attached photos, videos and
GIFs to disk.

## Usage

```
twimgdump <user-screen-name>
```

## Features

* Downloads all photos, videos and GIFs (read: also videos) from a Twitter user's media timeline.
* Uses the API the twitter.com website itself uses, meaning that no authentication or API keys are required.
* Encodes the user's screen name and the IDs of the tweets and media in the path of the downloaded files for easy
  reverse lookup.
* Supports downloading media from profiles marked as containing sensitive content.

## Limitations

* Only downloads media from a specific user; does not support downloading media matching an arbitrary search query.
* Very brittle; the program may terminate unexpectedly upon receiving non-successful or malformed responses.
* Does not currently handle rate limits at all (though this usually won't be a problem unless you're downloading a
  massive amount of media).
* Does not currently support downloading media from protected profiles.
* Subject to break if Twitter makes sudden changes to their API.

## Dependencies

* .NET Core 3.1

## Building

```
dotnet build [-c Release]
```

## Contributing

Contributions are welcome – if you encounter any problems or have any suggestions for improvements, feel free to
create a new issue or open a draft pull request.
