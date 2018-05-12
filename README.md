# MTProtoProxy
[![Telegram Channel](https://img.shields.io/badge/Channel-Telegram-blue.svg)](https://t.me/MTProtoProxy)



This is a project to create MTProto Proxy

This library is coded with C# using .NET Core framework to target Windows and Linux operating systems.

## Features

* **Easy to use**: you can easily use it
* **Completely managed**: connections are fully managed
* **High-performance**: processes a lot of connections
* **Fully-asynchronous**: it runs asynchronously

## How to use

        static void Main(string[] args)
        {
            var mtproto = new MTProtoProxyServer("secret", "port");
            mtproto.StartAsync();
            Console.ReadLine();
        }

## How to install

Create a .NETCore project and add the nuget package: `MTProtoProxy` or you can do it manually in you NuGet console package manager :

```
$> Install-Package MTProtoProxy
```
