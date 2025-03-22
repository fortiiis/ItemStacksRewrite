# ItemStacksRewrite
This is a rewrite of the popular [ItemStacks](https://github.com/mtnewton/valheim-mods/tree/master/ItemStacks) mod by MTNewton. Since they have not updated it in a while, this aims to continue support for the mod.

Item Stacks and Item Weights are now separated into different files, one massive file was annoying to work with in my opinion. A new folder in your configs path will be created "ItemStacksRewrite" here you'll find the stacks and weights configs. The general config can be found in the base config path.

NOTE* Configs generate first time when loading into a world!!

Differences between old mod:
- Server compatibility using [ServerSync](https://github.com/blaxxun-boop/ServerSync) by Blaxxun
- Cleaner configurations, separate files
- The old mod had things like "Abomination_attack3" in the weights section, this is due to how the mod was setup and Valheim itself. This rewrite cleans up the configs and removes unnecessary AI items that the player will never obtain.
- Toggleable mod item support. The old mod had support but wasn't toggleable. NOTE* If you disable mod items after generating the config entries, the entries will stay in the config file but will not be applied to modded items. You can re-generate the file with it false by deleting the file and re-loading into a world.

I do not take credit for the original mod idea or work done by MTNewton. This re-write has changed much of the original source code
If MTNewton returns and updates his mod, this will be taken down.
