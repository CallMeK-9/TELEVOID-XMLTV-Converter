# TELEVOID XMLTV Converter

This program is used to take XMLTV guide data and convert it into the custom JSON/image grid used by the TELEVOID TV System for VRChat. It ingests XMLTV data from a source URL and writes it into a guide.json and guide.jpg on disk.

It is intended to be used with a web server, like NGINX, to serve the resulting files.

This is to be considered early software. It has been tested on MacOS and Linux, but should work anywhere. If you encounter any issues with it, please reach out on Bluesky (@k-9.codes) or Discord (callmek9).


## Building

Typical dotnet build process. CD to project folder and run:

    dotnet build
Find the resulting executable in bin/Debug/net8.0.

## Usage

The most basic use is:

    ./XMLTV\ Converter -i https://your.source/iptv/xmltv.xml

This will create the following folders in your CWD, if they don't already exist:

    guide - where the resulting files will be written.
    replacementposters - where you can place posters for replacements
    cachedposters - where downloaded posters will be cached

If you need more flexibility, here's all the flags you can use:

       -o, --output: The path to output the guide JSON and JPG to. Defaults to ./guide
       -i, --input: The URL for the XMLTV guide information to convert. Required.
       -l, --length: The approximate maximum amount of hours of information to write for each channel. Defaults to 8.
       -r, --replacements: The path to the replacements JSON file. Defaults to ./replacements.json
       -p, --posters: The path to the replacements image folder. Defaults to ./replacementposters
       -c, --cache: The path to the poster image cache folder. Defaults to ./cachedposters

## Replacements

Replacements are used to match entries by title/name in the source XMLTV and overwrite their description and poster image in the resulting JSON. It is used primarily to add some rich data to entries whose sources would not include this information (local files on disk without metadata, such as custom entries, compilations, or programming blocks).

To use replacements, add a replacements.json file to the CWD. The format of the JSON is an array of objects each comprised of a name, description, and poster value, like so:

    [
	    {
		    "name": "Red vs. Blue",
		    "description": "Red vs. Blue episodes, seasons 1 - 8.\nCirca 2003 - 2010.",
		    "poster": "redvsblue.jpg"
	    },
	    {
		    "name": "Is It A Good Idea To Microwave This?",
		    "description": "Three dudes put random things into various microwaves and see what happens.\nCirca 2007-2011.",
		    "poster": "microwaveshow.jpg"
	    },
	    {
		    "name": "strongbad_email.exe",
		    "description": "Strongbad Email (sbemail) episodes, ripped from DVD.\nCirca 2001 - 2008.",
		    "poster": "sbemail.jpg"
	    }
	]

"name" is used to match the entry, "description" is self-explanatory, "poster" is the path to the corresponding poster image relative to the set replacement posters folder.

## Implementation

To publish your own TELEVOID TV-compatible feeds, you'll need:

 - A source of XMLTV data, up to 4 channels total. In most systems, this will be provided by [ErsatzTV](https://github.com/ErsatzTV/ErsatzTV), assembled separately.
 - An accessible web server. NGINX is recommended.

Once your ErsatzTV is set up and accessible, build this software and place it into a dedicated folder. This software is built to run once and then exit, so you'll need to set up a cron job or systemd timer to run it on an interval. I recommend every 5 minutes.

Finally, add a location directive to your NGINX config to serve the files out of the output folder, like so:

    location /guide {
		alias /opt/televoid-xmltv-converter/guide;
		try_files $uri $uri/ =404;
	}
Test to see if you can access https://your.domain/guide/guide.json and https://your.domain/guide/guide.jpg.
