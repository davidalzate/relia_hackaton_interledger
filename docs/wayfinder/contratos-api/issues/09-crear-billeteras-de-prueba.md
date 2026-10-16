# Crear las billeteras de prueba y confirmar COP

Type: task
Status: open
Blocked by: 03
Labels: wayfinder:task

## Question

El contrato de riel necesita saber con qué moneda y escala opera el demo. Eso solo se confirma creando las cuentas. HITL: el registro lo hace una persona. Checklist (procedimiento en `docs/research/testnet-cop.md`, rama `research/testnet-cop`):

1. Registrar tres usuarios en `wallet.interledger-test.dev` con correos distintos: Relia, Tienda Doña Rosa y el usuario demo.
2. En cada uno, intentar crear una cuenta en **COP**. Anotar si aparece en el selector.
3. Crear las wallet addresses: `relia`, `tienda-rosa` y `usuario-demo` (o las disponibles), y anotar el `assetCode` y el `assetScale` reales con `curl https://ilp.interledger-test.dev/<nombre>`.
4. Fondear la billetera de Relia con *Deposit* y anotar si el depósito en COP funciona.
5. Generar developer keys para la wallet address de Relia y guardar el key ID y `private.key` fuera del repo. Anotar dónde quedaron.
6. Si COP no está disponible: repetir en USD y registrar que se usará el plan B (tasa fija en el backend).

Resuelto cuando estén anotados los pointers reales, la moneda y escala, el saldo de Relia y dónde viven las llaves.
