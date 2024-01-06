# ServerInfo-for-LR-WEB
# RU: https://csdevs.net/resources/serverinfo-dlja-lr-web.553/

# EN:

Stumbled on Discord server on the monitoring module for the template Rich and decided to restore its work by rewriting the plugin to work in CS2.
Realized everything except getting rank and time on the server
The rank will be displayed only when working with the statistics plugin (https://github.com/ABKAM2023/CS2-RanksPoints)
Otherwise - I haven't touched the web module, everything should work as before but it's not for sure

![image](https://github.com/Stimayk/ServerInfo-for-LR-WEB/assets/51941742/b04497a7-a3aa-4ad4-a9ef-51e9bfc0c515)

Version 1.2 and above:
![image](https://github.com/Stimayk/ServerInfo-for-LR-WEB/assets/51941742/c83698d5-1052-402d-a972-da39a41f1fbe)


From the changes in the plugin:
Server information will be updated after a configurable time (in seconds) in server_info.ini
As I understood, previously it was done through Rcon, but in cs2 it seems to be absent or I do not know something, so I did it this way

The archive contains only the plugin, the module itself must be taken from the Discord server (https://discord.com/invite/sYKAk3GCbD)

Also slightly modified the installation in the archive, which I will duplicate below.

The code is 100% not perfect, there may be flaws and bugs, if any, please notify me with as much information as possible (logs, what you do, at what point, etc).
Suggestions for improvement are also welcome, maybe I will do something.

# Requirements:

LR WEB

Rich template

WEB Module (https://discord.com/invite/sYKAk3GCbD)

RanksPoints (https://github.com/ABKAM2023/CS2-RanksPoints) for rank display

CSSharp

# Commands:

css_getserverinfo - force server information update

# Installation:

1.Install the contents of the PLUGIN folder on the server /game/csgo/addons/counterstrikesharp/

    1.1.Customize the config /game/csgo/addons/counterstrikesharp/configs/server_info.ini

2.Download WEB module from https://discord.com/invite/sYKAk3GCbD

	2.1 Install the contents of the WEB folder on your site in the app/modules/ folder.

    2.2 Go to the forward folder in the js_controller.php file and change the password to the one you specified during configuration (line 5).

3.Restart the server and update modules and translations in the web admin panel
