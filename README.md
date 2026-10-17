# relia_hackaton_interledger

## Links de presentacion and video demo

[Presentacion](https://pagui.co/hackatoninterledger)

## Rail Proxy README

[/rail-proxy/README.md](./rail-proxy/README.md)

## Backend README

[/backend/README.md](./backend/README.md)

## Frontend README

[/frontend/relia/README.md](./frontend/relia/README.md)

## Arquitectura

[/docs/ARCHITECTURE.md](./docs/ARCHITECTURE.md)

## Uso de IA

[/AI_USAGE.md](./AI_USAGE.md)

## Correr todo en local

```sh
./dev.sh
```

Levanta en orden el Rail Proxy, PostgreSQL y el backend, y por último el frontend. Espera a que cada uno responda antes de seguir. Ctrl+C detiene los tres; PostgreSQL sigue en Docker. Requiere Node 22.12+, el SDK de .NET 10, Docker abierto y `rail-proxy/.env`. Sin ese `.env` arranca sin proxy.

| Pieza      | URL                                             | Cómo                                                   |
| ---------- | ----------------------------------------------- | ------------------------------------------------------ |
| Rail Proxy | `http://localhost:4000`                         | [rail-proxy/README.md](./rail-proxy/README.md)         |
| Backend    | `http://localhost:5080` (Swagger en `/swagger`) | [backend/README.md](./backend/README.md)               |
| Frontend   | `http://localhost:4200`                         | [frontend/relia/README.md](./frontend/relia/README.md) |
