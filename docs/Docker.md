# Docker Deployment

## Build
```bash
docker build -t amftpd .
```

## Run
```bash
docker run -d --name amftpd \
  -p 2121:2121 \
  -p 50000-50100:50000-50100 \
  -v $(pwd)/config:/config \
  -v $(pwd)/data:/data \
  amftpd /config/amftpd.json
```
