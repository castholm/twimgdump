# twimgdump

Just because you've liked or retweeted a piece of media on Twitter doesn't mean that it will still be there tomorrow.
The author might suddenly become permanently suspended.  Or they might have a lapse in judgment and irrevocably
delete a bunch of their old tweets.  Or they might be "canceled" for Americentric ideological reasons and be bullied
into deleting tweets deemed inappropriate.  Whatever the reason may be, the point is: if you like a user's uploaded
media, you probably want to take local copies of them.

*twimgdump* is a small command-line tool I developed to help me take local copies of art uploaded by artists I follow
on Twitter.  It retrieves all tweets posted to a Twitter user's media timeline and saves the attached photos, videos
and animated GIFs to disk.

## Usage

```
Usage:
    twimgdump [options] [--] <username>

Options:
    -c, --cursor <cursor>
        Sets the initial cursor value.

    -h, --help
        Displays this help text.

    -o, --output <output-file-path-template>
        Sets the template used to determine the output file paths of
        downloaded media.  The tokens '[user-id]', '[username]',
        '[tweet-id]', '[year]', '[month]', '[day]', '[hour]',
        '[minute]', '[second]', '[millisecond]', '[media-id]', '[stem]',
        '[extension]', '[index]', '[count]', '[width]' and '[height]'
        will be substituted by the corresponding attributes of retrieved
        media.

    -V, --version
        Displays the version number.
```

## Features

* Downloads all photos, videos and animated GIFs (read: also videos) posted to a Twitter user's media timeline using
  the highest resolution and quality options available.
* Uses the API the twitter.com website itself uses, meaning that no authentication or API keys are required.
* **New:** Offers granular control over the output file path of downloaded media using file path templates like
`[username]/[year]/[month]/[stem].[width]x[height][extension]`.
* Supports downloading media from profiles marked as containing sensitive content.

## Limitations

* Only downloads media from a specific profile; does not support downloading media matching an arbitrary search query.
* Very brittle; the program may terminate unexpectedly upon receiving non-successful or malformed responses.
* Does not currently handle rate limiting at all (though this usually won't be a problem unless you're downloading an
  enormous amount of media).
* Does not currently support downloading media from protected profiles.
* **Will** break if Twitter makes sudden changes to their API.

## Dependencies

* .NET Core 3.1

## Building

```
dotnet build [-c Release]
```

## Contributing

Contributions are welcome -- if you encounter any problems or have any suggestions for improvements, feel free to
create a new issue or open a draft pull request.
