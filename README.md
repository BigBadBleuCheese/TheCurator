![The Curator](The_Curator.jpg)

The Curator is the home-brew management bot for the Honor and Valor World of Warcraft Guild Discord Server.

- [Installing The Curator](#installing-the-curator)
- [Making Requests of The Curator](#making-requests-of-the-curator)
- [License](#license)
- [Contributing](#contributing)

# Installing The Curator
The Curator is built using Visual Studio 2022 / .NET 6.
Clone this repository and then, with the aforementioned tools installed, run the `TheCurator.ConsoleApp/Build.ps1` Powershell script.
The resulting publishing output will be in `TheCurator.ConsoleApp\bin\Release\net6.0\win-x64\publish`.
Copy this output to the Windows machine that will host The Curator.

Then, go to the [Discord Developer Portal](https://discord.com/developers/applications) and create a new bot.
Once you have invited it to your server, copy the token from the Bot page into a text file called `discordToken.txt` and place that in the same directory as the executible.
If you want The Curator to run automatically on the Windows host machine all the time (recommended), install it as a Windows Service by running this command in an elevated Command Prompt:
> sc create "The Curator" binpath= [Full Path to TheCurator.ConsoleApp.exe]

Use the Services MMC snap-in to start The Curator.
If the service won't start, check the Windows Event Log for details.

# Making Requests of The Curator

Just @ The Curator once you've added him to your Discord server and say `help` for a list of features and how to use them.

# License

[Apache 2.0 License](LICENSE)

# Contributing

[Click here](CONTRIBUTING.md) to learn how to contribute.