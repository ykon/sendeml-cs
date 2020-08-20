## About
### SendEML is a testing tool for sending raw eml files.
* SendEML-cs runs on .NET Core 3.1 or newer.
> [Download .NET Core](https://dotnet.microsoft.com/download/dotnet-core)

## Usage

### Windows
```
SendEML.exe <setting_file> ...
```
### Others
```
dotnet SendEML.dll <setting_file> ...
```

## Setting File (sample)
```
{
    "smtpHost": "172.16.3.151",
    "smtpPort": 25,
    "fromAddress": "a001@ah62.example.jp",
    "toAddresses": [
        "a001@ah62.example.jp",
        "a002@ah62.example.jp",
        "a003@ah62.example.jp"
    ],
    "emlFiles": [
        "test1.eml",
        "test2.eml",
        "test3.eml"
    ],
    "updateDate": true,
    "updateMessageId": true,
    "useParallel": false
}
```

## Options

* updateDate
  - Replace "Date:" line with the current date and time.

* updateMessageId
  - Replace "Message-ID:" line with a new random string ID.

* useParallel
  - Enable parallel processing for eml files.
