Benchmark Websocket, TCP and UDP in C#
=========================

Build Websocket Server
-------------

```bash
$ cd server-ws
$ dotnet publish -c Release -o ./publish
```

Run
```bash
$ dotnet run
```

Docker
```bash
$ cd server-ws
$ docker build -t server-ws .
$ docker run -p 3001:3001 server-ws
```

Build TCP/IP Server
-------------

```bash
$ cd server-tcp
$ dotnet publish -c Release -o ./publish
```

Run
```bash
$ dotnet run
```

Docker
```bash
$ cd server-tcp
$ docker build -t server-tcp .
$ docker run -p 4001:4001 server-tcp
```

Build UDP Server
-------------

```bash
$ cd server-udp
$ dotnet publish -c Release -o ./publish
```

Run
```bash
$ dotnet run
```

Docker
```bash
$ cd server-udp
$ docker build -t server-udp .
$ docker run -p 5001:5001 server-udp
```


Build Client
-------------

```bash
$ cd client
$ cargo build --release
```

Run
```bash
$ ./target/release/client-rust
```