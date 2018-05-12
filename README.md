# MTProtoProxy
[![Telegram Channel](https://img.shields.io/badge/Channel-Telegram-blue.svg)](https://t.me/MTProtoProxy)



This is a project to create MTProto Proxy for Telegram

This library is coded with C# using .NET Core framework to target Windows and Linux operating systems.

built by Visual Studio 2017

## Features

* **Easy to use**: you can easily use it
* **Completely managed**: connections are fully managed
* **High-performance**: processes a lot of connections
* **Fully-asynchronous**: it runs asynchronously

## Dependencies

 Library PCLCrypto

## How to use

        static void Main(string[] args)
        {
            var mtprotoProxy = new MTProtoProxyServer("secret", port);
            mtprotoProxy.StartAsync();
            Console.ReadLine();
        }
        
## How to generate random secret

        public static string GenerateRandomSecret()
        {
            return Guid.NewGuid().ToString().Replace("-", "");
        }

## How to install

Create a .NETCore project and add the nuget package: `MTProtoProxy` or you can do it manually in you NuGet console package manager :

```
$> Install-Package MTProtoProxy
```

## License

MIT License

Copyright (c) 2018 TGMTProto

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
