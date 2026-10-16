# Red de prueba de Interledger: ¿hay billeteras en COP y cómo se fondean?

Type: research
Status: resolved
Blocked by:
Labels: wayfinder:research

## Question

El demo opera solo en COP. En la wallet de prueba de Interledger (`wallet.interledger-test.dev`, payment pointers `$ilp.interledger-test.dev/...`):

- ¿Se puede crear una cuenta o billetera con `assetCode` COP? ¿Con qué `assetScale`?
- Si no se puede, ¿qué monedas hay y cómo cotiza el riel entre ellas (quotes multimoneda)? Opciones a comparar: levantar Rafiki local con COP, o usar USD/EUR con una tasa fija declarada en el backend.
- ¿Cómo se fondea una billetera de prueba (saldo de prueba) y hay límites?
- ¿Cómo se generan las llaves de desarrollador (key ID, llave privada) que el SDK necesita para firmar?

Resultado esperado: un sí o no a COP con evidencia, la alternativa recomendada si es no, y el procedimiento para crear y fondear las tres billeteras (Relia, corresponsal, usuario).

## Answer

Hallazgos completos en la rama local `research/testnet-cop` (commit fb5f001), archivo `docs/research/testnet-cop.md`. El push falló por falta de llave SSH con acceso al repo.

- **COP: probablemente sí, sin confirmar.** `interledger/testnet` agregó en mayo de 2026 la bóveda COP de GateHub sandbox (PR #2196, issue #2191: "COP para eventos/hackathons"). Que aparezca en el selector depende del asset COP en el Rafiki de `ilp.interledger-test.dev`, y eso solo se confirma registrándose.
- **assetScale**: no está declarado para COP. Se espera 2 porque todos los assets usan 2. Se confirma con un `curl` a la wallet address.
- **Una cuenta por moneda y por usuario**: para Relia, el corresponsal y el usuario en COP hacen falta tres usuarios con tres correos distintos.
- **Plan B**: las tres billeteras en USD con una tasa COP/USD fija declarada en el backend, sin conversión en el riel. Rafiki local con COP solo como respaldo offline.
- **Fondeo**: botón *Deposit* con dinero de juego vía GateHub sandbox. La UI advierte un límite diario de ~10.000 USD.
- **Llaves**: Settings → Developer Keys → *Generate*. Se genera un par Ed25519, `private.key` se descarga una sola vez y el key ID es un UUID. Las llaves van por wallet address.
- **Por confirmar al registrarse**: que COP esté en el selector, su scale, que el depósito en COP funcione, el límite diario en COP y si se aceptan alias de correo con `+tag`. → ticket [Crear las billeteras de prueba y confirmar COP](09-crear-billeteras-de-prueba.md).
