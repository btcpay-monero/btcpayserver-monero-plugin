# Generate a Monero primary address and obtain its private view key using monero-wallet-cli

## Overview

Note: This tutorial includes instructions for Windows, macOS, and Linux. Skip any steps that do not apply to your operating system.

In this tutorial, you will generate a Monero primary address and retrieve its associated private view key. These are commonly used when setting up a store or service that needs to receive Monero payments and monitor incoming transactions.

To accomplish this, we will use the Monero command-line wallet `monero-wallet-cli`. This tool is required to create the address and keys, and all wallet creation steps can be completed offline.

To begin, open a terminal window (Mac/Linux) or command prompt (Windows) and type the following commands:

```bash
# LINUX: Download the Linux 64-bit command line client and extract it
wget https://downloads.getmonero.org/linux64
tar -xvf linux64
cd monero-x86_64-linux-gnu-*

# MAC: Download the Mac command line client and extract it
wget https://downloads.getmonero.org/mac64
tar -xvf mac64
cd monero-x86_64-apple-*

# WINDOWS: Create a new folder with Windows File Explorer, and use your web browser to download the following file to the new folder
https://downloads.getmonero.org/win64

# You are strongly advised to verify the hashes of the archive you downloaded:
# https://www.getmonero.org/downloads/#cli

# WINDOWS: Double click the Monero zip file that has been downloaded to extract it. Then open Command Prompt. Use the 'cd' command to navigate to your new folder
cd monero-x86_64-w64-*

# Run the Monero command line wallet.
# LINUX/MAC:
./monero-wallet-cli --offline
# WINDOWS:
monero-wallet-cli.exe --offline

# Follow the instructions displayed to create a new wallet. When told the 25 word SEED, write this down on paper and keep it in a very safe place. Even if you forget your passwords, the 25 word SEED can be used to recreate your wallet from any machine and have complete control over your Monero funds. Sample output from the Monero wallet is below:

# Important: The wallet address, seed phrase, and keys shown below were generated solely for this tutorial and contain no funds. Do not reuse these values.

Specify wallet file name (e.g., MyWallet). If the wallet doesn't exist, it will be created.
Wallet file name (or Ctrl-C to quit): wallet
No wallet found with that name. Confirm creation of new wallet named: wallet
(Y/Yes/N/No): Y
Generating new wallet...
Enter a password for your new wallet:  ********
Confirm Password: ********
List of available languages for your wallet's seed:
0 : Deutsch
1 : English
2 : Español
3 : Français
4 : Italiano
5 : Nederlands
6 : Português
7 : русский язык
8 : 日本語
9 : 简体中文 (中国)
10 : Esperanto
11 : Lojban
Enter the number corresponding to the language of your choice: 1
Generated new wallet: 45zK2WxTctfc7h6qFVwoN4eJH1Wcu9spwDk2cCuivssre9sCu7uVEEmCziCkYvGyDwHHM1KNyrbid7zvWZ5XKzmJ5yMPTcE
View key: 8df34dd4b56bd13a69544d849bd0bce2a675bfc600d9e776ad801a6e9867580d
**********************************************************************
Your wallet has been generated!
To start synchronizing with the daemon, use the "refresh" command.
Use the "help" command to see a simplified list of available commands.
Use "help all" command to see the list of all available commands.
Use "help <command>" to see a command's documentation.
Always use the "exit" command when closing monero-wallet-cli to save 
your current session's state. Otherwise, you might need to synchronize 
your wallet again (your wallet keys are NOT at risk in any case).


NOTE: the following 25 words can be used to recover access to your wallet. Write them down and store them somewhere safe and secure. Please do not store them in your email or on file storage services outside of your immediate control.

arsenic ammo eating moisture fountain giant stunning eternal
neon ritual hookup wipeout zones launching voted sovereign
kiwi locker audio react inquest benches oyster present fountain
**********************************************************************
The daemon is not set up to background mine.
With background mining enabled, the daemon will mine when idle and not on battery.
Enabling this supports the network you are using, and makes you eligible for receiving new monero
Do you want to do it now? (Y/Yes/N/No): : n
Background mining not enabled. Set setup-background-mining to 1 to change.
If you are new to Monero, type "welcome" for a brief overview.
Error: wallet failed to connect to daemon, because it is set to offline mode
Background refresh thread started

# Type "address" to see your public wallet address. You can give this address to anyone, and they will be able to send you Monero. However, NEVER give anyone your 25 word SEED.

[wallet 45zK2W]: address
0  45zK2WxTctfc7h6qFVwoN4eJH1Wcu9spwDk2cCuivssre9sCu7uVEEmCziCkYvGyDwHHM1KNyrbid7zvWZ5XKzmJ5yMPTcE  Primary address 

# Type "viewkey" to see your public and private view key

[wallet 45zK2W]: viewkey
Wallet password: ********
secret: 8df34dd4b56bd13a69544d849bd0bce2a675bfc600d9e776ad801a6e9867580d
public: 1f562cd2ab81d247b6a5492b619d166157b30ca3d60ffe29d79ffd3fde1bbd2c
```

Congratulations! You have generated a Monero primary address along with its private view key, which can be used to create a view wallet and track incoming payments for your store.