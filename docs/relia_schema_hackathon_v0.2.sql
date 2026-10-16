-- =====================================================================
-- RELIA (hackathon Interledger) - Esquema PostgreSQL 16
-- Versión 0.2, 16 de octubre de 2026
--
-- Qué resuelve:
--   1. Inscribir wallets (payment pointers) con el celular como llave.
--   2. Manejar el cupo de cada corresponsal, sus operaciones, pagos,
--      historial y liquidaciones de comisión.
--
-- Convenciones:
--   * Dinero: pesos colombianos (COP) como bigint, sin decimales.
--   * Fechas: timestamptz (se guardan en UTC, se muestran en America/Bogota).
--   * Identificadores: uuid para entidades que viajan a la API, bigint
--     autoincremental para registros de auditoría.
--   * Nombres en español, plural, snake_case.
--   * Los estados y tipos son enums de PostgreSQL.
--
-- Reglas de negocio que el esquema hace cumplir:
--   * Tarifa por operación: $1.000, repartidos 500 Relia y 500 corresponsal.
--   * Una llave (celular + payment pointer) no se puede modificar ni borrar.
--   * Los movimientos de cupo y los eventos no se pueden editar ni borrar.
--   * Cupo, comisión y efectivo esperado nunca quedan en negativo.
--   * Efectivo: cada corresponsal tiene el efectivo que Relia espera en su
--     caja (no es un conteo). Sube con depósitos, baja con retiros y se
--     corrige con ajustes. La vista v_saldos_corresponsal cruza cupo,
--     comisión y efectivo contra las operaciones y marca descuadres.
--   * Cada movimiento se valida contra su operación o liquidación: el monto
--     del movimiento tiene que ser el que dice la operación.
--   * Una sola liquidación pendiente por corresponsal a la vez.
--   * Trazabilidad por wallet: cada pago guarda su payment pointer de origen
--     y de destino, los montos debitado y recibido del riel y sus
--     identificadores (que no se pueden repetir). Cada movimiento de cupo
--     apunta al pago completado que lo originó. La vista
--     v_trazabilidad_wallet muestra el recorrido de cualquier wallet.
--   * Los cambios de estado y las respuestas del riel quedan en bitácoras
--     de solo inserción (eventos_pago y eventos_operacion).
--   * Una operación no se puede alterar: sus montos, tarifa, tipo y wallet
--     quedan fijos desde que se crea, y una vez que sale de "pendiente" su
--     estado es final. Las operaciones tampoco se pueden borrar.
--   * Recibos: al completarse un depósito o un retiro se congela, en la
--     misma transacción, el contenido del recibo (tabla recibos) con un
--     código aleatorio y un hash. El recibo se puede volver a generar las
--     veces que se quiera con el mismo contenido; cada emisión queda
--     registrada (emisiones_recibo) y desde la segunda sale como copia.
--
-- Qué NO hace el esquema (lo hace la aplicación, dentro de UNA transacción
-- SQL con bloqueo de la fila de cupos):
--   * Actualizar cupos.cupo_actual, cupos.comision_acumulada y
--     cupos.efectivo_actual.
--   * Escribir el movimiento de cupo cuando una operación o liquidación
--     pasa a completada (y marcar antes la operación y el pago como
--     completados: el esquema valida el movimiento contra ambos).
--
-- Datos completos y máscaras: la base guarda siempre los datos completos
-- (celular, payment pointer). Ocultarlos parcialmente al mostrarlos es
-- responsabilidad del backend al devolver datos al frontend.
--
-- Tablas vacías: este script no inserta datos. Los inserts de ejemplo están
-- al final, comentados (sección 17).
--
-- Seguridad (repo público): las claves de corresponsales y admins están en
-- texto plano por decisión del hackathon. Solo usar claves de demo. Los
-- datos de ejemplo son ficticios.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 0. TIPOS ENUMERADOS
-- ---------------------------------------------------------------------

-- Qué hizo el corresponsal en una operación.
CREATE TYPE tipo_operacion AS ENUM (
    'deposito',     -- el usuario entrega efectivo y Relia paga a su wallet
    'retiro',       -- el usuario paga desde su wallet y recibe efectivo
    'carga_cupo'    -- el corresponsal recarga su cupo en Relia
);

-- Con qué medio el corresponsal carga cupo.
CREATE TYPE metodo_carga AS ENUM (
    'bre_b',        -- QR de Bre-B (simulado en el demo)
    'wallet'        -- solicitud de pago a su wallet por Open Payments
);

-- Estado de una operación o de un pago.
CREATE TYPE estado_operacion AS ENUM (
    'pendiente',    -- creada, esperando que el pago se complete
    'completada',   -- el pago se completó y el cupo ya se movió
    'fallida',      -- el riel respondió con error
    'vencida',      -- nadie aprobó a tiempo (por ejemplo, 5 minutos)
    'cancelada'     -- el corresponsal canceló antes de completarse
);

-- Estado de una liquidación de comisión.
CREATE TYPE estado_liquidacion AS ENUM (
    'pendiente',    -- creada, falta que el pago a la wallet se complete
    'pagada',       -- el pago se completó y la comisión ya se descontó
    'anulada'       -- se canceló antes de pagarse
);

-- Por qué riel viaja el dinero de un pago.
CREATE TYPE riel_pago AS ENUM (
    'open_payments',    -- Interledger Open Payments (red de prueba)
    'bre_b_simulado'    -- Bre-B simulado (en producción será Bre-B real)
);

-- Sentido del dinero visto desde la wallet de Relia.
CREATE TYPE direccion_pago AS ENUM (
    'entrante',     -- Relia recibe (retiro, carga de cupo)
    'saliente'      -- Relia paga (depósito, liquidación)
);

-- Canal del mensaje simulado.
CREATE TYPE canal_mensaje AS ENUM ('sms', 'whatsapp');

-- Para qué es el mensaje simulado.
CREATE TYPE tipo_mensaje AS ENUM (
    'otp',                  -- código de verificación de la vinculación
    'invitacion_wallet',    -- invitación a inscribir la wallet
    'aviso_deposito',       -- aviso al usuario de un depósito recibido
    'solicitud_pago',       -- aviso de una solicitud de pago por aprobar
    'comprobante',          -- comprobante enviado al usuario
    'aviso_comision'        -- aviso al corresponsal de que se le pagó su comisión
);

-- Qué generó un movimiento del libro de cupo.
CREATE TYPE tipo_movimiento AS ENUM (
    'deposito',
    'retiro',
    'carga_cupo',
    'liquidacion',  -- pago de la comisión acumulada al corresponsal
    'ajuste_efectivo'   -- saldo inicial o corrección del efectivo de la caja
);

-- Para qué se generó un código OTP.
CREATE TYPE proposito_otp AS ENUM (
    'vinculacion',      -- vincular un celular a un payment pointer
    'consulta_recibo'   -- que el usuario consulte sus recibos
);

-- Por dónde se mostró o envió un recibo.
CREATE TYPE canal_emision AS ENUM (
    'pantalla',     -- visto en la app
    'pdf',          -- descargado como PDF
    'impresion',    -- impreso desde la app
    'whatsapp',     -- enviado por WhatsApp (simulado en el demo)
    'sms'           -- enviado por SMS (simulado en el demo)
);

-- Quién pidió ver o generar un recibo.
CREATE TYPE solicitante_recibo AS ENUM (
    'corresponsal',     -- el corresponsal, desde su app
    'usuario',          -- el usuario, con su código de recibo y OTP
    'sistema'           -- un envío automático al completarse la operación
);

-- Quién provocó un cambio de estado.
CREATE TYPE origen_evento AS ENUM (
    'corresponsal',     -- una acción del corresponsal en la app
    'sistema',          -- un proceso interno (por ejemplo, vencimientos)
    'riel',             -- una respuesta de Open Payments o de Bre-B
    'simulador'         -- el panel de simuladores del demo
);

-- ---------------------------------------------------------------------
-- 0.1 FUNCIONES REUTILIZABLES
-- ---------------------------------------------------------------------

-- Actualiza actualizado_en en cada UPDATE de la fila.
CREATE FUNCTION set_actualizado_en() RETURNS trigger AS $$
BEGIN
    NEW.actualizado_en = now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Impide editar o borrar filas de tablas que solo se agregan.
-- No bloquea TRUNCATE, que es lo que usa el reinicio del demo.
CREATE FUNCTION bloquear_modificacion() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'La tabla % es de solo inserción: no se permite % de filas',
        TG_TABLE_NAME, TG_OP;
END;
$$ LANGUAGE plpgsql;

-- ---------------------------------------------------------------------
-- 1. TARIFAS
-- Tarifa vigente por operación, versionada para no alterar el pasado.
-- ---------------------------------------------------------------------
CREATE TABLE tarifas (
    -- Identificador de la versión de la tarifa.
    id                      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Lo que paga el usuario por cada depósito o retiro, en pesos.
    comision_total          bigint NOT NULL CHECK (comision_total > 0),

    -- Parte de la comisión que se queda Relia, en pesos.
    comision_relia          bigint NOT NULL CHECK (comision_relia >= 0),

    -- Parte de la comisión que se acumula para el corresponsal, en pesos.
    comision_corresponsal   bigint NOT NULL CHECK (comision_corresponsal >= 0),

    -- Desde cuándo rige esta tarifa.
    vigente_desde           timestamptz NOT NULL DEFAULT now(),

    -- Hasta cuándo rigió. NULL significa que es la tarifa actual.
    vigente_hasta           timestamptz,

    -- Cuándo se creó el registro.
    creado_en               timestamptz NOT NULL DEFAULT now(),

    -- Las dos partes deben sumar exactamente la comisión total.
    CONSTRAINT ck_tarifas_reparto
        CHECK (comision_total = comision_relia + comision_corresponsal),

    -- El rango de vigencia debe tener sentido.
    CONSTRAINT ck_tarifas_vigencia
        CHECK (vigente_hasta IS NULL OR vigente_hasta > vigente_desde)
);

-- Solo puede haber una tarifa actual (la que no tiene fecha de fin).
CREATE UNIQUE INDEX ux_tarifas_actual
    ON tarifas ((true)) WHERE vigente_hasta IS NULL;

COMMENT ON TABLE tarifas IS
    'Tarifa por operación, versionada. Cada operación copia sus valores.';

-- ---------------------------------------------------------------------
-- 2. CORRESPONSALES
-- Comercio, su acceso a la app y la wallet donde recibe sus pagos.
-- ---------------------------------------------------------------------
CREATE TABLE corresponsales (
    -- Identificador interno del corresponsal.
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Correo para iniciar sesión. Es único sin importar mayúsculas.
    correo              text NOT NULL
                        CHECK (correo ~ '^[^@[:space:]]+@[^@[:space:]]+$'),

    -- Nombre del comercio, como lo ve el corresponsal en la app.
    nombre              text NOT NULL CHECK (length(btrim(nombre)) > 0),

    -- Persona responsable del comercio (la que aparece en el panel admin).
    responsable         text NOT NULL CHECK (length(btrim(responsable)) > 0),

    -- Ciudad donde opera el comercio.
    ciudad              text NOT NULL CHECK (length(btrim(ciudad)) > 0),

    -- Dirección física del comercio.
    direccion           text NOT NULL,

    -- Celular del comercio, 10 dígitos. Es la LLAVE del corresponsal: se
    -- resuelve a su payment_pointer cuando carga cupo por wallet.
    telefono            text NOT NULL CHECK (telefono ~ '^3[0-9]{9}$'),

    -- Wallet del corresponsal en formato payment pointer ($dominio/ruta).
    -- Recibe las liquidaciones de comisión y es el origen de la carga
    -- de cupo por wallet. Es obligatoria: el corresponsal debe tener wallet.
    payment_pointer     text NOT NULL
                        CHECK (payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Clave de acceso EN TEXTO PLANO (decisión del hackathon). Solo claves
    -- de demo. Para producción se cambiaría por un hash (crypt de pgcrypto).
    clave               text NOT NULL CHECK (length(clave) >= 4),

    -- false desactiva al corresponsal sin borrar su historial.
    activo              boolean NOT NULL DEFAULT true,

    -- Cuándo se creó el registro.
    creado_en           timestamptz NOT NULL DEFAULT now(),

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en      timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_corresponsales_correo ON corresponsales (lower(correo));
CREATE UNIQUE INDEX ux_corresponsales_wallet ON corresponsales (payment_pointer);
CREATE UNIQUE INDEX ux_corresponsales_telefono ON corresponsales (telefono);

CREATE TRIGGER tg_corresponsales_actualizado
    BEFORE UPDATE ON corresponsales
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

COMMENT ON TABLE corresponsales IS
    'Comercios que operan efectivo con Relia. Incluye su acceso y su wallet.';

-- ---------------------------------------------------------------------
-- 2.1 ADMINS
-- Personal de Relia que entra al panel de administración y paga comisiones.
-- ---------------------------------------------------------------------
CREATE TABLE admins (
    -- Identificador interno del admin.
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Correo para iniciar sesión. Es único sin importar mayúsculas.
    correo          text NOT NULL
                    CHECK (correo ~ '^[^@[:space:]]+@[^@[:space:]]+$'),

    -- Nombre de la persona.
    nombre          text NOT NULL CHECK (length(btrim(nombre)) > 0),

    -- Clave de acceso EN TEXTO PLANO (decisión del hackathon). Solo claves de demo.
    clave           text NOT NULL CHECK (length(clave) >= 4),

    -- false desactiva al admin sin borrar su rastro.
    activo          boolean NOT NULL DEFAULT true,

    -- Cuándo se creó el registro.
    creado_en       timestamptz NOT NULL DEFAULT now(),

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en  timestamptz NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX ux_admins_correo ON admins (lower(correo));

CREATE TRIGGER tg_admins_actualizado
    BEFORE UPDATE ON admins
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

COMMENT ON TABLE admins IS
    'Administradores de Relia. Acceden al panel y pagan las comisiones.';

-- ---------------------------------------------------------------------
-- 3. CUPOS
-- Los totales de cada corresponsal: cupo, comisión y efectivo esperado.
-- Una fila por corresponsal. Se actualiza dentro de la misma transacción
-- SQL que escribe el movimiento (con SELECT ... FOR UPDATE sobre esta fila).
-- ---------------------------------------------------------------------
CREATE TABLE cupos (
    -- Corresponsal dueño del cupo. Es la llave primaria: una fila por corresponsal.
    corresponsal_id         uuid PRIMARY KEY
                            REFERENCES corresponsales (id) ON DELETE RESTRICT,

    -- Saldo operativo disponible en Relia, en pesos. Sube con las cargas y
    -- los retiros; baja con los depósitos. Nunca negativo.
    cupo_actual             bigint NOT NULL DEFAULT 0 CHECK (cupo_actual >= 0),

    -- Comisión ganada y todavía no liquidada, en pesos. Es lo que se paga
    -- en la próxima liquidación. Nunca negativa.
    comision_acumulada      bigint NOT NULL DEFAULT 0 CHECK (comision_acumulada >= 0),

    -- Total de comisión ya pagada al corresponsal a lo largo del tiempo.
    comision_liquidada_total bigint NOT NULL DEFAULT 0
                            CHECK (comision_liquidada_total >= 0),

    -- Efectivo que debería haber en la caja física según Relia, en pesos.
    -- NO es un conteo: resulta de las operaciones y los ajustes registrados.
    -- Sube con los depósitos (efectivo que entrega el usuario), baja con los
    -- retiros (efectivo que entrega el corresponsal) y se corrige con ajustes.
    -- Nunca negativo: si falta efectivo registrado hay que ajustarlo primero.
    efectivo_actual         bigint NOT NULL DEFAULT 0 CHECK (efectivo_actual >= 0),

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en          timestamptz NOT NULL DEFAULT now()
);

CREATE TRIGGER tg_cupos_actualizado
    BEFORE UPDATE ON cupos
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

COMMENT ON TABLE cupos IS
    'Totales de cada corresponsal: cupo, comisión y efectivo esperado. Una fila por corresponsal.';

-- Al crear un corresponsal se crea su fila de cupo en cero.
CREATE FUNCTION crear_cupo_inicial() RETURNS trigger AS $$
BEGIN
    INSERT INTO cupos (corresponsal_id) VALUES (NEW.id);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_corresponsales_cupo
    AFTER INSERT ON corresponsales
    FOR EACH ROW EXECUTE FUNCTION crear_cupo_inicial();

-- ---------------------------------------------------------------------
-- 4. LLAVES_WALLET
-- Inscripción de wallets: el celular del usuario es la llave de su
-- payment pointer. Una vez creada NO se puede cambiar ni borrar.
-- ---------------------------------------------------------------------
CREATE TABLE llaves_wallet (
    -- Identificador de la vinculación.
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Celular colombiano de 10 dígitos, sin indicativo ni espacios.
    -- Ejemplo: 3000000001. Es la llave: un celular, una sola wallet.
    celular             text NOT NULL CHECK (celular ~ '^3[0-9]{9}$'),

    -- Wallet del usuario en formato payment pointer ($dominio/ruta).
    payment_pointer     text NOT NULL
                        CHECK (payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Dirección https equivalente del payment pointer. Es la que usa
    -- Open Payments para encontrar la wallet. Se calcula sola.
    wallet_address_url  text GENERATED ALWAYS AS
                        ('https://' || substr(payment_pointer, 2)) STORED,

    -- Cuándo el usuario confirmó el OTP y quedó vinculado.
    vinculada_en        timestamptz NOT NULL DEFAULT now(),

    -- Un celular tiene una sola wallet.
    CONSTRAINT ux_llaves_celular UNIQUE (celular),

    -- Una wallet pertenece a un solo celular.
    CONSTRAINT ux_llaves_wallet UNIQUE (wallet_address_url)
);

CREATE TRIGGER tg_llaves_inmutables
    BEFORE UPDATE OR DELETE ON llaves_wallet
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE llaves_wallet IS
    'Celular vinculado a un payment pointer. Inmutable. Borrar solo con TRUNCATE en el reinicio del demo.';

-- ---------------------------------------------------------------------
-- 5. DESAFIOS_OTP
-- Códigos OTP de la vinculación de wallet y de la consulta de recibos.
-- Se guarda el hash, nunca el código.
-- ---------------------------------------------------------------------
CREATE TABLE desafios_otp (
    -- Identificador del desafío.
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Celular al que se envió el código.
    celular             text NOT NULL CHECK (celular ~ '^3[0-9]{9}$'),

    -- Para qué se generó el código.
    proposito           proposito_otp NOT NULL DEFAULT 'vinculacion',

    -- Payment pointer que el usuario quiere vincular a ese celular.
    -- Solo se convierte en llave si el OTP se verifica. Solo se llena
    -- cuando el propósito es la vinculación.
    payment_pointer     text
                        CHECK (payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Hash del código de 6 dígitos (por ejemplo SHA-256 con sal).
    codigo_hash         text NOT NULL,

    -- Intentos fallidos de verificación hasta ahora.
    intentos            smallint NOT NULL DEFAULT 0 CHECK (intentos >= 0),

    -- Máximo de intentos permitidos antes de invalidar el código.
    max_intentos        smallint NOT NULL DEFAULT 5 CHECK (max_intentos > 0),

    -- Cuándo deja de servir el código.
    expira_en           timestamptz NOT NULL,

    -- Cuándo se verificó con éxito. NULL si no se ha usado.
    verificado_en       timestamptz,

    -- Cuándo se generó el código.
    creado_en           timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_otp_expiracion CHECK (expira_en > creado_en),
    CONSTRAINT ck_otp_intentos CHECK (intentos <= max_intentos),

    -- La vinculación exige payment pointer; la consulta de recibos no lo usa.
    CONSTRAINT ck_otp_proposito
        CHECK ((proposito = 'vinculacion') = (payment_pointer IS NOT NULL))
);

-- Para buscar el último código enviado a un celular.
CREATE INDEX ix_otp_celular ON desafios_otp (celular, creado_en DESC);

COMMENT ON TABLE desafios_otp IS
    'OTP de vinculación de wallet y de consulta de recibos. Código guardado como hash, con expiración e intentos.';

-- ---------------------------------------------------------------------
-- 6. OPERACIONES
-- Cada depósito, retiro o carga de cupo. Es la "transacción" de negocio.
-- Los montos se calculan con estas reglas (M = monto_solicitado):
--   Sin valor redondo: neto = M - comisión,  cobrado = M
--   Con valor redondo: neto = M,             cobrado = M + comisión
-- ---------------------------------------------------------------------
CREATE TABLE operaciones (
    -- Identificador de la operación (viaja a la API).
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Consecutivo interno que genera el número visible.
    numero                  bigint GENERATED ALWAYS AS IDENTITY,

    -- Número legible para mostrar al corresponsal, por ejemplo OP-000123.
    numero_visible          text GENERATED ALWAYS AS
                            ('OP-' || lpad(numero::text, 6, '0')) STORED,

    -- Corresponsal que hace la operación.
    corresponsal_id         uuid NOT NULL
                            REFERENCES corresponsales (id) ON DELETE RESTRICT,

    -- Qué clase de operación es.
    tipo                    tipo_operacion NOT NULL,

    -- Solo para carga de cupo: por Bre-B o por wallet. NULL en las demás.
    metodo_carga            metodo_carga,

    -- Wallet del usuario que participa (depósito y retiro). NULL en carga de cupo.
    llave_wallet_id         uuid REFERENCES llaves_wallet (id) ON DELETE RESTRICT,

    -- Monto que escribió el corresponsal (M), en pesos.
    monto_solicitado        bigint NOT NULL CHECK (monto_solicitado > 0),

    -- true si el usuario quiere recibir el valor redondo y pagar la comisión aparte.
    valor_redondo           boolean NOT NULL DEFAULT false,

    -- Tarifa usada. NULL en carga de cupo, que no cobra comisión.
    tarifa_id               bigint REFERENCES tarifas (id) ON DELETE RESTRICT,

    -- COPIA de la comisión cobrada al usuario cuando se hizo la operación.
    comision_total          bigint NOT NULL DEFAULT 0 CHECK (comision_total >= 0),

    -- COPIA de la parte de la comisión que es de Relia.
    comision_relia          bigint NOT NULL DEFAULT 0 CHECK (comision_relia >= 0),

    -- COPIA de la parte de la comisión que es del corresponsal.
    comision_corresponsal   bigint NOT NULL DEFAULT 0 CHECK (comision_corresponsal >= 0),

    -- Lo que recibe el usuario: dinero en su wallet (depósito) o efectivo
    -- (retiro). En carga de cupo es el monto cargado.
    monto_neto              bigint NOT NULL CHECK (monto_neto > 0),

    -- Lo que paga el usuario: efectivo (depósito) o pago por wallet
    -- (retiro), igual al neto más la comisión. En carga de cupo es el monto cargado.
    monto_cobrado           bigint NOT NULL CHECK (monto_cobrado > 0),

    -- Estado actual de la operación.
    estado                  estado_operacion NOT NULL DEFAULT 'pendiente',

    -- Clave que envía la app para que repetir la misma petición no cree
    -- una segunda operación (evita entregar efectivo dos veces).
    clave_idempotencia      text NOT NULL,

    -- Cuándo se creó la operación.
    creado_en               timestamptz NOT NULL DEFAULT now(),

    -- Cuándo se completó. Solo tiene valor si el estado es completada.
    completada_en           timestamptz,

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en          timestamptz NOT NULL DEFAULT now(),

    -- Evita duplicar una operación del mismo corresponsal.
    CONSTRAINT ux_operaciones_idempotencia
        UNIQUE (corresponsal_id, clave_idempotencia),

    -- La carga de cupo no cobra comisión, no tiene wallet de usuario,
    -- exige método y el neto y el cobrado son el monto cargado.
    CONSTRAINT ck_op_carga CHECK (
        tipo <> 'carga_cupo' OR (
            metodo_carga IS NOT NULL
            AND llave_wallet_id IS NULL
            AND tarifa_id IS NULL
            AND NOT valor_redondo
            AND comision_total = 0 AND comision_relia = 0
            AND comision_corresponsal = 0
            AND monto_neto = monto_solicitado
            AND monto_cobrado = monto_solicitado
        )
    ),

    -- Depósito y retiro: exigen wallet de usuario y tarifa, el monto debe
    -- superar la comisión, el reparto debe sumar y el cálculo de neto y
    -- cobrado debe seguir la regla del valor redondo.
    CONSTRAINT ck_op_deposito_retiro CHECK (
        tipo = 'carga_cupo' OR (
            metodo_carga IS NULL
            AND llave_wallet_id IS NOT NULL
            AND tarifa_id IS NOT NULL
            AND monto_solicitado > comision_total
            AND comision_total = comision_relia + comision_corresponsal
            AND monto_cobrado = monto_neto + comision_total
            AND (
                (valor_redondo AND monto_neto = monto_solicitado)
                OR (NOT valor_redondo
                    AND monto_neto = monto_solicitado - comision_total)
            )
        )
    ),

    -- completada_en solo existe cuando el estado es completada.
    CONSTRAINT ck_op_completada
        CHECK ((estado = 'completada') = (completada_en IS NOT NULL))
);

-- Historial de un corresponsal, el más reciente primero.
CREATE INDEX ix_operaciones_corresponsal
    ON operaciones (corresponsal_id, creado_en DESC);

-- Para encontrar rápido las operaciones que siguen pendientes.
CREATE INDEX ix_operaciones_pendientes
    ON operaciones (creado_en) WHERE estado = 'pendiente';

CREATE TRIGGER tg_operaciones_actualizado
    BEFORE UPDATE ON operaciones
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

-- Candado de la operación: nada de lo que define la operación se puede
-- cambiar después de creada, y una vez que sale de "pendiente" su estado
-- es final. Solo estado y completada_en pueden cambiar, una vez, desde
-- "pendiente". Tampoco se pueden borrar operaciones.
CREATE FUNCTION proteger_operacion() RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Las operaciones no se pueden borrar (%)', OLD.numero_visible;
    END IF;

    IF NEW.id IS DISTINCT FROM OLD.id
       OR NEW.numero IS DISTINCT FROM OLD.numero
       OR NEW.corresponsal_id IS DISTINCT FROM OLD.corresponsal_id
       OR NEW.tipo IS DISTINCT FROM OLD.tipo
       OR NEW.metodo_carga IS DISTINCT FROM OLD.metodo_carga
       OR NEW.llave_wallet_id IS DISTINCT FROM OLD.llave_wallet_id
       OR NEW.monto_solicitado IS DISTINCT FROM OLD.monto_solicitado
       OR NEW.valor_redondo IS DISTINCT FROM OLD.valor_redondo
       OR NEW.tarifa_id IS DISTINCT FROM OLD.tarifa_id
       OR NEW.comision_total IS DISTINCT FROM OLD.comision_total
       OR NEW.comision_relia IS DISTINCT FROM OLD.comision_relia
       OR NEW.comision_corresponsal IS DISTINCT FROM OLD.comision_corresponsal
       OR NEW.monto_neto IS DISTINCT FROM OLD.monto_neto
       OR NEW.monto_cobrado IS DISTINCT FROM OLD.monto_cobrado
       OR NEW.clave_idempotencia IS DISTINCT FROM OLD.clave_idempotencia
       OR NEW.creado_en IS DISTINCT FROM OLD.creado_en THEN
        RAISE EXCEPTION 'La operación % no se puede modificar: sus datos quedan fijos al crearla',
            OLD.numero_visible;
    END IF;

    IF OLD.estado <> 'pendiente'
       AND (NEW.estado <> OLD.estado
            OR NEW.completada_en IS DISTINCT FROM OLD.completada_en) THEN
        RAISE EXCEPTION 'La operación % ya está en estado final (%) y no puede cambiar',
            OLD.numero_visible, OLD.estado;
    END IF;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_operaciones_proteger
    BEFORE UPDATE OR DELETE ON operaciones
    FOR EACH ROW EXECUTE FUNCTION proteger_operacion();

COMMENT ON TABLE operaciones IS
    'Depósitos, retiros y cargas de cupo. Copia la tarifa y los montos calculados. Sus datos no cambian después de creada.';

-- ---------------------------------------------------------------------
-- 7. MENSAJES_SIMULADOS
-- SMS y WhatsApp del demo. Alimenta la bandeja del teléfono del usuario.
-- ---------------------------------------------------------------------
CREATE TABLE mensajes_simulados (
    -- Identificador del mensaje.
    id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Por dónde se "envió".
    canal           canal_mensaje NOT NULL,

    -- Para qué es el mensaje.
    tipo            tipo_mensaje NOT NULL,

    -- Celular que recibe el mensaje (10 dígitos).
    destinatario    text NOT NULL CHECK (destinatario ~ '^3[0-9]{9}$'),

    -- Texto completo del mensaje.
    contenido       text NOT NULL,

    -- Enlace incluido en el mensaje (invitación de WhatsApp, aprobación de pago).
    enlace          text,

    -- Operación relacionada, si el mensaje avisa de una.
    operacion_id    uuid REFERENCES operaciones (id) ON DELETE SET NULL,

    -- true cuando el usuario ya abrió el mensaje en el simulador.
    leido           boolean NOT NULL DEFAULT false,

    -- Cuándo se creó el mensaje.
    creado_en       timestamptz NOT NULL DEFAULT now()
);

-- Bandeja de un celular, el más reciente primero.
CREATE INDEX ix_mensajes_destinatario
    ON mensajes_simulados (destinatario, creado_en DESC);

COMMENT ON TABLE mensajes_simulados IS
    'Bandeja simulada de SMS y WhatsApp. Solo existe en el demo.';

-- ---------------------------------------------------------------------
-- 8. LIQUIDACIONES
-- Pago manual y mensual de TODA la comisión pendiente del corresponsal,
-- enviado a su wallet por Open Payments.
-- ---------------------------------------------------------------------
CREATE TABLE liquidaciones (
    -- Identificador de la liquidación.
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Consecutivo interno que genera el número visible.
    numero              bigint GENERATED ALWAYS AS IDENTITY,

    -- Número legible, por ejemplo LQ-000007.
    numero_visible      text GENERATED ALWAYS AS
                        ('LQ-' || lpad(numero::text, 6, '0')) STORED,

    -- Corresponsal al que se le paga.
    corresponsal_id     uuid NOT NULL
                        REFERENCES corresponsales (id) ON DELETE RESTRICT,

    -- Admin de Relia que inició el pago.
    creada_por_admin_id uuid NOT NULL
                        REFERENCES admins (id) ON DELETE RESTRICT,

    -- Wallet a la que se paga, copiada del corresponsal al crear la liquidación.
    payment_pointer_destino text NOT NULL
                        CHECK (payment_pointer_destino ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Primer día cubierto por esta liquidación.
    periodo_desde       date NOT NULL,

    -- Último día cubierto por esta liquidación.
    periodo_hasta       date NOT NULL,

    -- Total de comisión pendiente que se paga, en pesos. Siempre es todo
    -- el saldo de comisión_acumulada al momento de crearla.
    monto               bigint NOT NULL CHECK (monto > 0),

    -- Estado de la liquidación.
    estado              estado_liquidacion NOT NULL DEFAULT 'pendiente',

    -- Cuándo se confirmó el pago. Solo tiene valor si el estado es pagada.
    pagada_en           timestamptz,

    -- Razón de la anulación. Solo tiene valor si el estado es anulada.
    motivo_anulacion    text,

    -- Cuándo se creó la liquidación.
    creado_en           timestamptz NOT NULL DEFAULT now(),

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en      timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_liq_periodo CHECK (periodo_hasta >= periodo_desde),
    CONSTRAINT ck_liq_pagada CHECK ((estado = 'pagada') = (pagada_en IS NOT NULL)),
    CONSTRAINT ck_liq_anulada CHECK ((estado = 'anulada') = (motivo_anulacion IS NOT NULL))
);

-- Una sola liquidación pendiente por corresponsal: evita pagar dos
-- veces la misma comisión.
CREATE UNIQUE INDEX ux_liquidaciones_una_pendiente
    ON liquidaciones (corresponsal_id) WHERE estado = 'pendiente';

CREATE INDEX ix_liquidaciones_corresponsal
    ON liquidaciones (corresponsal_id, creado_en DESC);

CREATE TRIGGER tg_liquidaciones_actualizado
    BEFORE UPDATE ON liquidaciones
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

COMMENT ON TABLE liquidaciones IS
    'Pago manual mensual de la comisión total pendiente, a la wallet del corresponsal.';

-- ---------------------------------------------------------------------
-- 9. PAGOS
-- Cada intento de mover dinero por un riel. Una operación puede tener
-- varios pagos (por ejemplo, un intento vencido y un reintento). También
-- cubre el pago de una liquidación. Guarda los datos del riel, no los
-- secretos.
-- ---------------------------------------------------------------------
CREATE TABLE pagos (
    -- Identificador del pago.
    id                      uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Operación a la que pertenece. NULL si es el pago de una liquidación.
    operacion_id            uuid REFERENCES operaciones (id) ON DELETE RESTRICT,

    -- Liquidación a la que pertenece. NULL si es el pago de una operación.
    liquidacion_id          uuid REFERENCES liquidaciones (id) ON DELETE RESTRICT,

    -- Riel por el que viaja el dinero.
    riel                    riel_pago NOT NULL,

    -- Sentido del dinero visto desde Relia: entrante si Relia recibe, saliente si paga.
    direccion               direccion_pago NOT NULL,

    -- Estado del pago.
    estado                  estado_operacion NOT NULL DEFAULT 'pendiente',

    -- Monto del pago en pesos.
    monto                   bigint NOT NULL CHECK (monto > 0),

    -- Moneda. Por ahora solo COP.
    moneda                  char(3) NOT NULL DEFAULT 'COP' CHECK (moneda = 'COP'),

    -- Wallet de la que sale el dinero (quien paga), en formato payment pointer.
    -- Se copia aquí para que el rastro no dependa de datos que luego cambien.
    payment_pointer_origen  text NOT NULL
                            CHECK (payment_pointer_origen ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Wallet a la que llega el dinero (quien recibe), en formato payment pointer.
    payment_pointer_destino text NOT NULL
                            CHECK (payment_pointer_destino ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'),

    -- Open Payments: URL del incoming payment (el cobro creado en la wallet que recibe).
    op_incoming_payment_url text,

    -- Open Payments: URL del quote (la cotización del pago).
    op_quote_url            text,

    -- Open Payments: URL del outgoing payment (el pago que sale de la wallet que paga).
    op_outgoing_payment_url text,

    -- Open Payments: URL donde la persona aprueba el pago en su wallet
    -- (interacción del grant). NULL si no se requiere aprobación.
    url_aprobacion          text,

    -- Open Payments: URL para continuar el grant después de la aprobación.
    grant_continue_uri      text,

    -- Open Payments: token para continuar el grant. SE GUARDA CIFRADO
    -- (por ejemplo con pgp_sym_encrypt de pgcrypto), nunca en claro.
    grant_continue_token_cifrado bytea,

    -- Bre-B simulado: identificador del QR o cobro simulado.
    referencia_riel         text,

    -- Monto que se DEBITA de la wallet de origen, tal como lo maneja el riel
    -- (Open Payments usa texto con unidades mínimas). Sirve para conciliar.
    monto_debito_valor      text,

    -- Código de la moneda del monto debitado (assetCode de Open Payments).
    monto_debito_codigo     text,

    -- Escala del monto debitado (assetScale: posiciones decimales).
    monto_debito_escala     smallint,

    -- Monto que RECIBE la wallet de destino, tal como lo maneja el riel.
    -- Puede diferir del debitado si el riel cobra costos o convierte.
    monto_recibido_valor    text,

    -- Código de la moneda del monto recibido (assetCode de Open Payments).
    monto_recibido_codigo   text,

    -- Escala del monto recibido (assetScale: posiciones decimales).
    monto_recibido_escala   smallint,

    -- Hasta cuándo se puede aprobar el pago (por ejemplo, 5 minutos).
    vence_en                timestamptz,

    -- Código de error que devolvió el riel, si falló.
    error_codigo            text,

    -- Mensaje de error del riel, si falló.
    error_mensaje           text,

    -- Datos adicionales del riel que no tienen columna propia.
    detalle_riel            jsonb NOT NULL DEFAULT '{}'::jsonb,

    -- Cuándo se creó el pago.
    creado_en               timestamptz NOT NULL DEFAULT now(),

    -- Cuándo se completó. Solo tiene valor si el estado es completada.
    completado_en           timestamptz,

    -- Última modificación de la fila (la mantiene un trigger).
    actualizado_en          timestamptz NOT NULL DEFAULT now(),

    -- Todo pago pertenece a una operación o a una liquidación, nunca a ambas.
    CONSTRAINT ck_pagos_padre
        CHECK ((operacion_id IS NULL) <> (liquidacion_id IS NULL)),

    CONSTRAINT ck_pagos_completado
        CHECK ((estado = 'completada') = (completado_en IS NOT NULL)),

    -- Origen y destino no pueden ser la misma wallet.
    CONSTRAINT ck_pagos_wallets_distintas
        CHECK (payment_pointer_origen <> payment_pointer_destino),

    -- El monto debitado se guarda completo (valor, moneda y escala) o no se guarda.
    CONSTRAINT ck_pagos_debito_completo CHECK (
        (monto_debito_valor IS NULL) = (monto_debito_codigo IS NULL)
        AND (monto_debito_valor IS NULL) = (monto_debito_escala IS NULL)
    ),

    -- El monto recibido se guarda completo (valor, moneda y escala) o no se guarda.
    CONSTRAINT ck_pagos_recibido_completo CHECK (
        (monto_recibido_valor IS NULL) = (monto_recibido_codigo IS NULL)
        AND (monto_recibido_valor IS NULL) = (monto_recibido_escala IS NULL)
    )
);

CREATE INDEX ix_pagos_operacion ON pagos (operacion_id) WHERE operacion_id IS NOT NULL;
CREATE INDEX ix_pagos_liquidacion ON pagos (liquidacion_id) WHERE liquidacion_id IS NOT NULL;

-- Para el proceso que marca como vencidos los pagos pendientes.
CREATE INDEX ix_pagos_vencimiento ON pagos (vence_en) WHERE estado = 'pendiente';

-- Para consultar todo lo que pasó con una wallet, como origen o como destino.
CREATE INDEX ix_pagos_origen ON pagos (payment_pointer_origen, creado_en DESC);
CREATE INDEX ix_pagos_destino ON pagos (payment_pointer_destino, creado_en DESC);

-- Un mismo pago del riel solo puede existir una vez en Relia. Evita acreditar
-- dos veces el cupo por procesar dos veces la misma confirmación.
CREATE UNIQUE INDEX ux_pagos_incoming_url
    ON pagos (op_incoming_payment_url) WHERE op_incoming_payment_url IS NOT NULL;
CREATE UNIQUE INDEX ux_pagos_outgoing_url
    ON pagos (op_outgoing_payment_url) WHERE op_outgoing_payment_url IS NOT NULL;

CREATE TRIGGER tg_pagos_actualizado
    BEFORE UPDATE ON pagos
    FOR EACH ROW EXECUTE FUNCTION set_actualizado_en();

COMMENT ON TABLE pagos IS
    'Intentos de pago por riel (Open Payments o Bre-B simulado) de operaciones y liquidaciones.';

-- ---------------------------------------------------------------------
-- 10. EVENTOS_PAGO
-- Bitácora de cada pago: cambios de estado y cada consulta o llamada al
-- riel, con la respuesta cruda. Es la evidencia para conciliar y depurar.
-- Solo se agrega.
-- ---------------------------------------------------------------------
CREATE TABLE eventos_pago (
    -- Identificador del evento.
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Pago al que pertenece el evento.
    pago_id             uuid NOT NULL
                        REFERENCES pagos (id) ON DELETE RESTRICT,

    -- Qué se hizo o qué se observó (por ejemplo: crear_incoming_payment,
    -- crear_quote, aprobar_grant, consultar_estado, vencer).
    accion              text NOT NULL,

    -- Estado del pago antes del evento. NULL en el evento de creación.
    estado_anterior     estado_operacion,

    -- Estado del pago después del evento. Igual al anterior si solo se
    -- consultó (polling) y no hubo cambio.
    estado_nuevo        estado_operacion NOT NULL,

    -- Quién provocó el evento.
    origen              origen_evento NOT NULL,

    -- Código HTTP de la respuesta del riel, si hubo llamada.
    codigo_http         smallint,

    -- Respuesta cruda del riel para auditoría. No debe incluir tokens ni secretos.
    respuesta_riel      jsonb,

    -- Explicación libre del evento.
    detalle             text,

    -- Cuándo ocurrió el evento.
    creado_en           timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_eventos_pago ON eventos_pago (pago_id, id);

CREATE TRIGGER tg_eventos_pago_inmutables
    BEFORE UPDATE OR DELETE ON eventos_pago
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE eventos_pago IS
    'Bitácora de solo inserción de cada pago: llamadas al riel, respuestas y cambios de estado.';

-- ---------------------------------------------------------------------
-- 11. EVENTOS_OPERACION
-- Bitácora de cambios de estado de cada operación. Solo se agrega.
-- ---------------------------------------------------------------------
CREATE TABLE eventos_operacion (
    -- Identificador del evento.
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Operación a la que pertenece el evento.
    operacion_id        uuid NOT NULL
                        REFERENCES operaciones (id) ON DELETE RESTRICT,

    -- Estado antes del cambio. NULL en el evento de creación.
    estado_anterior     estado_operacion,

    -- Estado después del cambio.
    estado_nuevo        estado_operacion NOT NULL,

    -- Quién provocó el cambio.
    origen              origen_evento NOT NULL,

    -- Explicación libre del cambio (por ejemplo "usuario rechazó el pago").
    detalle             text,

    -- Cuándo ocurrió el cambio.
    creado_en           timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_eventos_operacion ON eventos_operacion (operacion_id, id);

CREATE TRIGGER tg_eventos_inmutables
    BEFORE UPDATE OR DELETE ON eventos_operacion
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE eventos_operacion IS
    'Bitácora de solo inserción con cada cambio de estado de una operación.';

-- ---------------------------------------------------------------------
-- 12. MOVIMIENTOS_CUPO
-- Libro del cupo, la comisión y el efectivo. Una fila por operación
-- completada, por liquidación pagada o por ajuste de efectivo. Es el
-- historial que ve el corresponsal.
-- Cada movimiento apunta al pago exacto que lo originó (salvo los ajustes).
-- Solo se agrega: un error se corrige con un movimiento nuevo.
-- ---------------------------------------------------------------------
CREATE TABLE movimientos_cupo (
    -- Identificador del movimiento. Su orden es el orden contable.
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Corresponsal al que pertenece el movimiento.
    corresponsal_id     uuid NOT NULL
                        REFERENCES corresponsales (id) ON DELETE RESTRICT,

    -- Qué lo originó.
    tipo                tipo_movimiento NOT NULL,

    -- Operación que lo originó. NULL si fue una liquidación.
    operacion_id        uuid REFERENCES operaciones (id) ON DELETE RESTRICT,

    -- Liquidación que lo originó. NULL si fue una operación.
    liquidacion_id      uuid REFERENCES liquidaciones (id) ON DELETE RESTRICT,

    -- Pago exacto, completado, que originó el movimiento. Si la operación
    -- tuvo varios intentos, aquí queda el que sí se completó. NULL solo en
    -- los ajustes de efectivo, que no pasan por un riel.
    pago_id             uuid REFERENCES pagos (id) ON DELETE RESTRICT,

    -- Cupo justo antes del movimiento.
    cupo_antes          bigint NOT NULL CHECK (cupo_antes >= 0),

    -- Cuánto cambió el cupo. Negativo en depósitos, positivo en retiros
    -- y cargas, cero en liquidaciones.
    variacion_cupo      bigint NOT NULL,

    -- Cupo justo después del movimiento.
    cupo_despues        bigint NOT NULL CHECK (cupo_despues >= 0),

    -- Comisión acumulada justo antes del movimiento.
    comision_antes      bigint NOT NULL CHECK (comision_antes >= 0),

    -- Cuánto cambió la comisión acumulada. Positivo en depósitos y retiros
    -- (la del corresponsal), cero en cargas, negativo en liquidaciones.
    variacion_comision  bigint NOT NULL,

    -- Comisión acumulada justo después del movimiento.
    comision_despues    bigint NOT NULL CHECK (comision_despues >= 0),

    -- Efectivo esperado en la caja justo antes del movimiento.
    efectivo_antes      bigint NOT NULL CHECK (efectivo_antes >= 0),

    -- Cuánto cambió el efectivo. Positivo en depósitos (entra el efectivo del
    -- usuario), negativo en retiros (sale efectivo al usuario), cero en
    -- cargas y liquidaciones, y distinto de cero en los ajustes.
    variacion_efectivo  bigint NOT NULL,

    -- Efectivo esperado en la caja justo después del movimiento.
    efectivo_despues    bigint NOT NULL CHECK (efectivo_despues >= 0),

    -- Explicación del ajuste de efectivo (por ejemplo "Efectivo inicial" o
    -- "Consignación al banco"). Solo se llena en los ajustes.
    motivo              text,

    -- Cuándo se registró el movimiento.
    creado_en           timestamptz NOT NULL DEFAULT now(),

    -- Las cuentas deben cuadrar.
    CONSTRAINT ck_mov_cupo CHECK (cupo_despues = cupo_antes + variacion_cupo),
    CONSTRAINT ck_mov_comision
        CHECK (comision_despues = comision_antes + variacion_comision),

    CONSTRAINT ck_mov_efectivo
        CHECK (efectivo_despues = efectivo_antes + variacion_efectivo),

    -- Un movimiento viene de una operación (con su pago), de una liquidación
    -- (con su pago) o es un ajuste de efectivo (sin pago y con motivo).
    CONSTRAINT ck_mov_origen CHECK (
        (tipo = 'liquidacion'
            AND liquidacion_id IS NOT NULL AND operacion_id IS NULL
            AND pago_id IS NOT NULL AND motivo IS NULL)
        OR (tipo IN ('deposito', 'retiro', 'carga_cupo')
            AND operacion_id IS NOT NULL AND liquidacion_id IS NULL
            AND pago_id IS NOT NULL AND motivo IS NULL)
        OR (tipo = 'ajuste_efectivo'
            AND operacion_id IS NULL AND liquidacion_id IS NULL
            AND pago_id IS NULL AND length(btrim(coalesce(motivo, ''))) > 0)
    ),

    -- El signo de cada variación depende del tipo. En depósitos y retiros el
    -- efectivo se mueve en espejo del cupo: lo que entra de uno sale del otro.
    CONSTRAINT ck_mov_signos CHECK (
        (tipo = 'deposito' AND variacion_cupo < 0 AND variacion_comision > 0
            AND variacion_efectivo = -variacion_cupo)
        OR (tipo = 'retiro' AND variacion_cupo > 0 AND variacion_comision > 0
            AND variacion_efectivo = -variacion_cupo)
        OR (tipo = 'carga_cupo' AND variacion_cupo > 0 AND variacion_comision = 0
            AND variacion_efectivo = 0)
        OR (tipo = 'liquidacion' AND variacion_cupo = 0 AND variacion_comision < 0
            AND variacion_efectivo = 0)
        OR (tipo = 'ajuste_efectivo' AND variacion_cupo = 0
            AND variacion_comision = 0 AND variacion_efectivo <> 0)
    )
);

-- Cada operación y cada liquidación generan como máximo un movimiento.
CREATE UNIQUE INDEX ux_mov_operacion
    ON movimientos_cupo (operacion_id) WHERE operacion_id IS NOT NULL;
CREATE UNIQUE INDEX ux_mov_liquidacion
    ON movimientos_cupo (liquidacion_id) WHERE liquidacion_id IS NOT NULL;

-- Un pago solo puede acreditar o debitar el cupo una vez.
CREATE UNIQUE INDEX ux_mov_pago ON movimientos_cupo (pago_id);

-- Historial de un corresponsal en orden contable inverso.
CREATE INDEX ix_mov_corresponsal ON movimientos_cupo (corresponsal_id, id DESC);

-- Valida cada movimiento contra lo que dicen su pago, su operación o su
-- liquidación: el pago debe estar completado y ser de la misma operación o
-- liquidación, y el monto del movimiento debe ser el de la operación.
-- Los ajustes de efectivo no tienen pago ni operación y se aceptan tal cual.
CREATE FUNCTION validar_movimiento_pago() RETURNS trigger AS $$
DECLARE
    p pagos%ROWTYPE;
    o operaciones%ROWTYPE;
    l liquidaciones%ROWTYPE;
    cupo_esperado bigint;
BEGIN
    IF NEW.tipo = 'ajuste_efectivo' THEN
        RETURN NEW;
    END IF;

    -- El pago debe estar completado y pertenecer a lo que se está registrando.
    SELECT * INTO p FROM pagos WHERE id = NEW.pago_id;
    IF p.estado <> 'completada' THEN
        RAISE EXCEPTION 'El pago % no está completado (estado: %)', p.id, p.estado;
    END IF;
    IF p.operacion_id IS DISTINCT FROM NEW.operacion_id
       OR p.liquidacion_id IS DISTINCT FROM NEW.liquidacion_id THEN
        RAISE EXCEPTION 'El pago % no pertenece a la operación o liquidación del movimiento', p.id;
    END IF;

    IF NEW.operacion_id IS NOT NULL THEN
        -- La operación debe ser del mismo corresponsal, estar completada y
        -- ser del mismo tipo; el cupo y la comisión deben moverse lo que ella dice.
        SELECT * INTO o FROM operaciones WHERE id = NEW.operacion_id;
        IF o.corresponsal_id <> NEW.corresponsal_id THEN
            RAISE EXCEPTION 'La operación % es de otro corresponsal', o.numero_visible;
        END IF;
        IF o.estado <> 'completada' THEN
            RAISE EXCEPTION 'La operación % no está completada (estado: %)', o.numero_visible, o.estado;
        END IF;
        IF o.tipo::text <> NEW.tipo::text THEN
            RAISE EXCEPTION 'La operación % es de tipo % y el movimiento de tipo %', o.numero_visible, o.tipo, NEW.tipo;
        END IF;
        cupo_esperado := CASE o.tipo
            WHEN 'deposito' THEN -o.monto_cobrado   -- el cupo baja lo que recibió en efectivo
            ELSE o.monto_neto                       -- retiro y carga: el cupo sube
        END;
        IF NEW.variacion_cupo <> cupo_esperado THEN
            RAISE EXCEPTION 'El cupo debe variar % según la operación % y varía %',
                cupo_esperado, o.numero_visible, NEW.variacion_cupo;
        END IF;
        IF NEW.variacion_comision <> o.comision_corresponsal THEN
            RAISE EXCEPTION 'La comisión debe variar % según la operación % y varía %',
                o.comision_corresponsal, o.numero_visible, NEW.variacion_comision;
        END IF;
    ELSE
        -- La liquidación debe ser del mismo corresponsal, estar pagada y
        -- descontar exactamente su monto de la comisión acumulada.
        SELECT * INTO l FROM liquidaciones WHERE id = NEW.liquidacion_id;
        IF l.corresponsal_id <> NEW.corresponsal_id THEN
            RAISE EXCEPTION 'La liquidación % es de otro corresponsal', l.numero_visible;
        END IF;
        IF l.estado <> 'pagada' THEN
            RAISE EXCEPTION 'La liquidación % no está pagada (estado: %)', l.numero_visible, l.estado;
        END IF;
        IF NEW.variacion_comision <> -l.monto THEN
            RAISE EXCEPTION 'La comisión debe variar % según la liquidación % y varía %',
                -l.monto, l.numero_visible, NEW.variacion_comision;
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_mov_valida_pago
    BEFORE INSERT ON movimientos_cupo
    FOR EACH ROW EXECUTE FUNCTION validar_movimiento_pago();

CREATE TRIGGER tg_mov_inmutables
    BEFORE UPDATE OR DELETE ON movimientos_cupo
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE movimientos_cupo IS
    'Libro de solo inserción del cupo, la comisión y el efectivo: antes, variación y después.';

-- ---------------------------------------------------------------------
-- 13. RECIBOS Y EMISIONES_RECIBO
-- El recibo de un depósito o un retiro se CONGELA al completarse la
-- operación: se guarda su contenido tal como era en ese momento (nombre del
-- comercio, montos, wallet, referencia del pago). Aunque luego el corresponsal
-- cambie de nombre o de dirección, el recibo se vuelve a generar idéntico.
-- Cada vez que se muestra, descarga o envía queda una emisión; desde la
-- segunda sale marcada como copia.
-- ---------------------------------------------------------------------
CREATE TABLE recibos (
    -- Identificador del recibo.
    id              uuid PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Operación a la que corresponde. Una operación tiene un solo recibo.
    operacion_id    uuid NOT NULL UNIQUE
                    REFERENCES operaciones (id) ON DELETE RESTRICT,

    -- Código aleatorio de 12 caracteres (0-9, A-F) para consultar el recibo
    -- por enlace o QR. No es consecutivo, así que no se puede adivinar. Para
    -- verlo, el usuario además verifica su celular con un OTP.
    codigo          text NOT NULL UNIQUE
                    DEFAULT upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 12))
                    CHECK (codigo ~ '^[0-9A-F]{12}$'),

    -- Contenido congelado del recibo en formato JSON: número, tipo, fecha,
    -- comercio, usuario (celular y wallet completos; el backend los enmascara
    -- al mostrarlos), montos y referencia del pago.
    datos           jsonb NOT NULL,

    -- Huella SHA-256 (64 caracteres hexadecimales) del contenido. Permite
    -- comprobar que el contenido no fue alterado.
    hash_contenido  text NOT NULL CHECK (hash_contenido ~ '^[0-9a-f]{64}$'),

    -- Cuándo se emitió el recibo original.
    creado_en       timestamptz NOT NULL DEFAULT now()
);

CREATE TRIGGER tg_recibos_inmutables
    BEFORE UPDATE OR DELETE ON recibos
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE recibos IS
    'Contenido congelado del recibo de cada depósito o retiro completado. Solo inserción.';

CREATE TABLE emisiones_recibo (
    -- Identificador de la emisión.
    id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Recibo que se mostró, descargó o envió.
    recibo_id           uuid NOT NULL
                        REFERENCES recibos (id) ON DELETE RESTRICT,

    -- Por dónde salió: pantalla, PDF, WhatsApp o SMS.
    canal               canal_emision NOT NULL,

    -- Quién lo pidió.
    solicitado_por      solicitante_recibo NOT NULL,

    -- Corresponsal que lo pidió. Obligatorio si lo pidió el corresponsal.
    corresponsal_id     uuid REFERENCES corresponsales (id) ON DELETE RESTRICT,

    -- Celular del usuario que lo pidió, verificado con OTP. Obligatorio si
    -- lo pidió el usuario.
    celular_usuario     text CHECK (celular_usuario ~ '^3[0-9]{9}$'),

    -- true si no es la primera emisión: el documento debe decir "COPIA".
    -- La calcula un trigger; no hace falta enviarla.
    es_copia            boolean NOT NULL DEFAULT false,

    -- Cuándo se emitió.
    creado_en           timestamptz NOT NULL DEFAULT now(),

    CONSTRAINT ck_emision_solicitante CHECK (
        (solicitado_por = 'corresponsal' AND corresponsal_id IS NOT NULL)
        OR (solicitado_por = 'usuario' AND celular_usuario IS NOT NULL)
        OR (solicitado_por = 'sistema')
    )
);

CREATE INDEX ix_emisiones_recibo ON emisiones_recibo (recibo_id, id);

-- Marca como copia toda emisión que no sea la primera del recibo.
CREATE FUNCTION marcar_copia_recibo() RETURNS trigger AS $$
BEGIN
    NEW.es_copia := EXISTS (SELECT 1 FROM emisiones_recibo WHERE recibo_id = NEW.recibo_id);
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_emisiones_copia
    BEFORE INSERT ON emisiones_recibo
    FOR EACH ROW EXECUTE FUNCTION marcar_copia_recibo();

CREATE TRIGGER tg_emisiones_inmutables
    BEFORE UPDATE OR DELETE ON emisiones_recibo
    FOR EACH ROW EXECUTE FUNCTION bloquear_modificacion();

COMMENT ON TABLE emisiones_recibo IS
    'Registro de cada vez que un recibo se muestra, descarga o envía. Desde la segunda es copia.';

-- Arma y guarda el recibo de una operación completada. Solo aplica a
-- depósitos y retiros (la carga de cupo no genera recibo al usuario).
-- Se ejecuta sola cuando la operación pasa a completada.
CREATE FUNCTION emitir_recibo(p_operacion_id uuid) RETURNS recibos AS $$
DECLARE
    o operaciones%ROWTYPE;
    c corresponsales%ROWTYPE;
    w llaves_wallet%ROWTYPE;
    p pagos%ROWTYPE;
    contenido jsonb;
    r recibos%ROWTYPE;
BEGIN
    SELECT * INTO o FROM operaciones WHERE id = p_operacion_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'No existe la operación %', p_operacion_id;
    END IF;
    IF o.estado <> 'completada' THEN
        RAISE EXCEPTION 'La operación % no está completada: no tiene recibo', o.numero_visible;
    END IF;
    IF o.tipo = 'carga_cupo' THEN
        RAISE EXCEPTION 'La carga de cupo (%) no genera recibo para el usuario', o.numero_visible;
    END IF;

    SELECT * INTO c FROM corresponsales WHERE id = o.corresponsal_id;
    SELECT * INTO w FROM llaves_wallet WHERE id = o.llave_wallet_id;
    SELECT * INTO p FROM pagos
    WHERE operacion_id = o.id AND estado = 'completada'
    ORDER BY completado_en DESC, creado_en DESC LIMIT 1;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'La operación % no tiene un pago completado', o.numero_visible;
    END IF;

    contenido := jsonb_build_object(
        'numero', o.numero_visible,
        'tipo', o.tipo,
        'fecha', to_char(o.completada_en AT TIME ZONE 'America/Bogota', 'YYYY-MM-DD"T"HH24:MI:SS'),
        'moneda', 'COP',
        'comercio', jsonb_build_object(
            'nombre', c.nombre, 'direccion', c.direccion, 'telefono', c.telefono),
        'usuario', jsonb_build_object(
            'celular', w.celular,
            'payment_pointer', w.payment_pointer),
        'montos', jsonb_build_object(
            'monto_solicitado', o.monto_solicitado,
            'valor_redondo', o.valor_redondo,
            'comision', o.comision_total,
            'monto_neto', o.monto_neto,
            'monto_cobrado', o.monto_cobrado),
        'pago', jsonb_build_object(
            'riel', p.riel,
            'referencia', COALESCE(p.op_outgoing_payment_url, p.op_incoming_payment_url, p.referencia_riel))
    );

    INSERT INTO recibos (operacion_id, datos, hash_contenido)
    VALUES (o.id, contenido, encode(sha256(convert_to(contenido::text, 'UTF8')), 'hex'))
    RETURNING * INTO r;
    RETURN r;
END;
$$ LANGUAGE plpgsql;

-- Dispara la emisión del recibo en la misma transacción en que la operación
-- se completa. Por eso el pago debe estar completado antes que la operación.
CREATE FUNCTION emitir_recibo_al_completar() RETURNS trigger AS $$
BEGIN
    IF NEW.estado = 'completada' AND OLD.estado <> 'completada'
       AND NEW.tipo IN ('deposito', 'retiro') THEN
        PERFORM emitir_recibo(NEW.id);
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER tg_operaciones_emitir_recibo
    AFTER UPDATE OF estado ON operaciones
    FOR EACH ROW EXECUTE FUNCTION emitir_recibo_al_completar();

-- Comprueba que el contenido de un recibo no fue alterado: recalcula la
-- huella y la compara con la guardada. Devuelve NULL si el código no existe.
CREATE FUNCTION recibo_integro(p_codigo text) RETURNS boolean AS $$
    SELECT encode(sha256(convert_to(datos::text, 'UTF8')), 'hex') = hash_contenido
    FROM recibos WHERE codigo = upper(p_codigo);
$$ LANGUAGE sql STABLE;

-- ---------------------------------------------------------------------
-- 14. VISTA V_TRAZABILIDAD_WALLET
-- Recorrido completo de cualquier payment pointer: una fila por pago y por
-- cada lado en que la wallet participa. Para ver todo lo de una wallet:
--   SELECT * FROM v_trazabilidad_wallet
--   WHERE payment_pointer = '$ilp.interledger-test.dev/usuario-demo'
--   ORDER BY creado_en;
-- ---------------------------------------------------------------------
CREATE VIEW v_trazabilidad_wallet AS
SELECT
    w.payment_pointer,                  -- wallet consultada
    w.lado,                             -- 'origen' (pagó) o 'destino' (recibió)
    p.id AS pago_id,                    -- pago concreto
    p.riel,                             -- riel usado
    p.direccion,                        -- sentido visto desde Relia
    p.estado,                           -- estado del pago
    p.monto,                            -- monto en pesos
    p.moneda,                           -- moneda
    p.payment_pointer_origen,           -- quién pagó
    p.payment_pointer_destino,          -- quién recibió
    o.id AS operacion_id,               -- operación relacionada, si existe
    o.numero_visible AS operacion,      -- número legible de la operación
    o.tipo AS tipo_operacion,           -- depósito, retiro o carga de cupo
    l.id AS liquidacion_id,             -- liquidación relacionada, si existe
    l.numero_visible AS liquidacion,    -- número legible de la liquidación
    p.op_incoming_payment_url,          -- identificador del cobro en el riel
    p.op_outgoing_payment_url,          -- identificador del pago en el riel
    p.creado_en,                        -- cuándo se creó el pago
    p.completado_en                     -- cuándo se completó
FROM pagos p
CROSS JOIN LATERAL (
    VALUES (p.payment_pointer_origen, 'origen'),
           (p.payment_pointer_destino, 'destino')
) AS w (payment_pointer, lado)
LEFT JOIN operaciones o ON o.id = p.operacion_id
LEFT JOIN liquidaciones l ON l.id = p.liquidacion_id;

COMMENT ON VIEW v_trazabilidad_wallet IS
    'Recorrido de cada payment pointer: pagos donde participó como origen o destino.';

-- ---------------------------------------------------------------------
-- 15. VISTA V_SALDOS_CORRESPONSAL
-- Los totales de cada corresponsal (cupo, comisión y efectivo) junto con lo
-- que resulta de recalcularlos desde las operaciones completadas. Si todo
-- está bien, las columnas descuadre_* valen 0. Es la tarjeta de totales
-- de la app y también un control de integridad.
-- ---------------------------------------------------------------------
CREATE VIEW v_saldos_corresponsal AS
SELECT
    c.id AS corresponsal_id,                -- corresponsal
    c.nombre,                               -- nombre del comercio
    s.cupo_actual,                          -- cupo disponible (guardado)
    s.comision_acumulada,                   -- comisión pendiente de liquidar (guardada)
    s.comision_liquidada_total,             -- comisión ya pagada (guardada)
    s.efectivo_actual,                      -- efectivo esperado en caja (guardado)

    o.efectivo_recibido_depositos,          -- suma del efectivo que entregaron los usuarios
    o.efectivo_entregado_retiros,           -- suma del efectivo que se entregó a los usuarios
    a.ajustes_efectivo,                     -- suma de los ajustes de efectivo
    o.cupo_cargado,                         -- suma de las cargas de cupo
    o.comision_generada,                    -- comisión total ganada por operaciones

    -- Efectivo que resulta de las operaciones y los ajustes.
    o.efectivo_recibido_depositos - o.efectivo_entregado_retiros + a.ajustes_efectivo
        AS efectivo_calculado,

    -- Cupo que resulta de las operaciones.
    o.cupo_cargado + o.efectivo_entregado_retiros - o.efectivo_recibido_depositos
        AS cupo_calculado,

    -- Diferencias entre lo guardado y lo calculado. Deben ser 0.
    s.efectivo_actual
        - (o.efectivo_recibido_depositos - o.efectivo_entregado_retiros + a.ajustes_efectivo)
        AS descuadre_efectivo,
    s.cupo_actual
        - (o.cupo_cargado + o.efectivo_entregado_retiros - o.efectivo_recibido_depositos)
        AS descuadre_cupo,
    (s.comision_acumulada + s.comision_liquidada_total) - o.comision_generada
        AS descuadre_comision
FROM corresponsales c
JOIN cupos s ON s.corresponsal_id = c.id
CROSS JOIN LATERAL (
    SELECT
        COALESCE(SUM(monto_cobrado) FILTER (WHERE tipo = 'deposito'), 0)
            AS efectivo_recibido_depositos,
        COALESCE(SUM(monto_neto) FILTER (WHERE tipo = 'retiro'), 0)
            AS efectivo_entregado_retiros,
        COALESCE(SUM(monto_neto) FILTER (WHERE tipo = 'carga_cupo'), 0)
            AS cupo_cargado,
        COALESCE(SUM(comision_corresponsal), 0)
            AS comision_generada
    FROM operaciones
    WHERE corresponsal_id = c.id AND estado = 'completada'
) o
CROSS JOIN LATERAL (
    SELECT COALESCE(SUM(variacion_efectivo), 0) AS ajustes_efectivo
    FROM movimientos_cupo
    WHERE corresponsal_id = c.id AND tipo = 'ajuste_efectivo'
) a;

COMMENT ON VIEW v_saldos_corresponsal IS
    'Totales del corresponsal (cupo, comisión, efectivo) y su recálculo desde las operaciones. Los descuadres deben ser 0.';

-- ---------------------------------------------------------------------
-- 16. VISTAS DE HISTORIAL Y DE PANEL ADMIN
-- ---------------------------------------------------------------------

-- 16.1 HISTORIAL DEL CORRESPONSAL
-- Lo que ve el corresponsal (y el admin en el detalle): una fila por
-- operación en cualquier estado, por ajuste de efectivo y por liquidación.
-- Las filas completadas toman antes y después de su movimiento. Las
-- fallidas, vencidas, canceladas y pendientes dejan antes = después, con el
-- saldo del último movimiento anterior a ellas. El motivo sale de la
-- bitácora de la operación o del error del riel. Cada fila se abre en su
-- comprobante. Filtrar por corresponsal_id y ordenar por fecha DESC.
CREATE VIEW v_historial_corresponsal AS
-- Depósitos, retiros y cargas de cupo, en todos sus estados.
SELECT
    o.corresponsal_id,
    COALESCE(o.completada_en, o.actualizado_en) AS fecha,   -- cuándo ocurrió o se cerró
    o.tipo::text AS tipo,                       -- deposito, retiro o carga_cupo
    o.numero_visible AS referencia,             -- número legible
    o.id AS operacion_id,
    NULL::uuid AS liquidacion_id,
    m.id AS movimiento_id,                      -- NULL si no se completó
    o.estado,
    o.monto_solicitado AS valor,                -- el valor M de la operación
    COALESCE(m.cupo_antes, u.cupo_despues, 0) AS cupo_antes,
    COALESCE(m.cupo_despues, u.cupo_despues, 0) AS cupo_despues,
    COALESCE(m.comision_antes, u.comision_despues, 0) AS comision_antes,
    COALESCE(m.comision_despues, u.comision_despues, 0) AS comision_despues,
    COALESCE(m.efectivo_antes, u.efectivo_despues, 0) AS efectivo_antes,
    COALESCE(m.efectivo_despues, u.efectivo_despues, 0) AS efectivo_despues,
    CASE WHEN o.estado = 'completada' THEN NULL
         ELSE COALESCE(ev.detalle, pg.error_mensaje) END AS motivo
FROM operaciones o
LEFT JOIN movimientos_cupo m ON m.operacion_id = o.id
LEFT JOIN LATERAL (
    SELECT x.cupo_despues, x.comision_despues, x.efectivo_despues
    FROM movimientos_cupo x
    WHERE x.corresponsal_id = o.corresponsal_id AND x.creado_en <= o.actualizado_en
    ORDER BY x.id DESC LIMIT 1
) u ON m.id IS NULL
LEFT JOIN LATERAL (
    SELECT e.detalle FROM eventos_operacion e
    WHERE e.operacion_id = o.id AND e.estado_nuevo = o.estado
    ORDER BY e.id DESC LIMIT 1
) ev ON true
LEFT JOIN LATERAL (
    SELECT q.error_mensaje FROM pagos q
    WHERE q.operacion_id = o.id AND q.error_mensaje IS NOT NULL
    ORDER BY q.creado_en DESC LIMIT 1
) pg ON true

UNION ALL
-- Ajustes de efectivo (asignar o fijar el efectivo de la caja).
SELECT
    m.corresponsal_id, m.creado_en, 'ajuste_efectivo', NULL, NULL, NULL, m.id,
    'completada'::estado_operacion,
    m.variacion_efectivo,
    m.cupo_antes, m.cupo_despues, m.comision_antes, m.comision_despues,
    m.efectivo_antes, m.efectivo_despues,
    m.motivo
FROM movimientos_cupo m
WHERE m.tipo = 'ajuste_efectivo'

UNION ALL
-- Pagos de comisión (liquidaciones), en todos sus estados.
SELECT
    l.corresponsal_id,
    COALESCE(l.pagada_en, l.actualizado_en),
    'liquidacion', l.numero_visible, NULL, l.id, m.id,
    (CASE l.estado WHEN 'pagada' THEN 'completada'
                   WHEN 'anulada' THEN 'cancelada'
                   ELSE 'pendiente' END)::estado_operacion,
    l.monto,
    COALESCE(m.cupo_antes, u.cupo_despues, 0),
    COALESCE(m.cupo_despues, u.cupo_despues, 0),
    COALESCE(m.comision_antes, u.comision_despues, 0),
    COALESCE(m.comision_despues, u.comision_despues, 0),
    COALESCE(m.efectivo_antes, u.efectivo_despues, 0),
    COALESCE(m.efectivo_despues, u.efectivo_despues, 0),
    CASE WHEN l.estado = 'pagada' THEN NULL
         ELSE COALESCE(l.motivo_anulacion, pg.error_mensaje) END
FROM liquidaciones l
LEFT JOIN movimientos_cupo m ON m.liquidacion_id = l.id
LEFT JOIN LATERAL (
    SELECT x.cupo_despues, x.comision_despues, x.efectivo_despues
    FROM movimientos_cupo x
    WHERE x.corresponsal_id = l.corresponsal_id AND x.creado_en <= l.actualizado_en
    ORDER BY x.id DESC LIMIT 1
) u ON m.id IS NULL
LEFT JOIN LATERAL (
    SELECT q.error_mensaje FROM pagos q
    WHERE q.liquidacion_id = l.id AND q.error_mensaje IS NOT NULL
    ORDER BY q.creado_en DESC LIMIT 1
) pg ON true;

COMMENT ON VIEW v_historial_corresponsal IS
    'Historial del corresponsal: operaciones en todos los estados, ajustes de efectivo y pagos de comisión, con antes y después.';

-- 16.2 RESUMEN POR CORRESPONSAL PARA EL PANEL ADMIN
-- Una fila por corresponsal con lo que muestra la tabla del panel. Los
-- totales cuentan solo operaciones completadas y usan el valor M
-- (monto_solicitado), no el monto cobrado.
CREATE VIEW v_resumen_admin_corresponsal AS
SELECT
    c.id AS corresponsal_id,
    c.nombre,
    c.responsable,
    c.ciudad,
    c.payment_pointer,                          -- wallet donde se paga la comisión
    s.cupo_actual,
    s.efectivo_actual,
    s.comision_acumulada AS comision_por_pagar,
    s.comision_liquidada_total AS comision_pagada_total,
    o.operaciones,                              -- cantidad de depósitos y retiros completados
    o.depositos_cantidad,
    o.depositos_valor,                          -- suma de M en depósitos
    o.retiros_cantidad,
    o.retiros_valor,                            -- suma de M en retiros
    o.depositos_valor + o.retiros_valor AS total_movido,
    COALESCE(l.pagada_hoy, 0) AS comision_pagada_hoy
FROM corresponsales c
JOIN cupos s ON s.corresponsal_id = c.id
CROSS JOIN LATERAL (
    SELECT
        COUNT(*) AS operaciones,
        COUNT(*) FILTER (WHERE tipo = 'deposito') AS depositos_cantidad,
        COALESCE(SUM(monto_solicitado) FILTER (WHERE tipo = 'deposito'), 0) AS depositos_valor,
        COUNT(*) FILTER (WHERE tipo = 'retiro') AS retiros_cantidad,
        COALESCE(SUM(monto_solicitado) FILTER (WHERE tipo = 'retiro'), 0) AS retiros_valor
    FROM operaciones
    WHERE corresponsal_id = c.id AND estado = 'completada' AND tipo IN ('deposito', 'retiro')
) o
CROSS JOIN LATERAL (
    SELECT SUM(monto) AS pagada_hoy
    FROM liquidaciones
    WHERE corresponsal_id = c.id AND estado = 'pagada'
      AND (pagada_en AT TIME ZONE 'America/Bogota')::date
          = (now() AT TIME ZONE 'America/Bogota')::date
) l;

COMMENT ON VIEW v_resumen_admin_corresponsal IS
    'Fila por corresponsal para el panel admin: cupo, efectivo, comisión por pagar y totales movidos (valor M).';

COMMIT;

-- ---------------------------------------------------------------------
-- 17. INSERTS DE EJEMPLO (comentados: el script deja las tablas vacías)
-- Para cargar los datos de demo, quita los "-- " de las líneas siguientes y
-- ejecútalas. Todo es ficticio. Rosa empieza en ceros: sin cupo, sin
-- efectivo, sin comisión y sin historial.
-- ---------------------------------------------------------------------

-- -- Tarifa vigente: $1.000 por operación, 500 Relia y 500 corresponsal.
-- INSERT INTO tarifas (comision_total, comision_relia, comision_corresponsal)
-- VALUES (1000, 500, 500);
--
-- -- Admin de Relia.
-- INSERT INTO admins (correo, nombre, clave)
-- VALUES ('admin@relia.co', 'Admin Relia', 'admin1234');
--
-- -- Corresponsales. La fila de cupo de cada uno se crea sola, en cero.
-- -- El celular (telefono) es la llave del corresponsal.
-- INSERT INTO corresponsales (correo, nombre, responsable, ciudad, direccion, telefono, payment_pointer, clave) VALUES
-- ('rosa@demo.relia',     'Tienda Doña Rosa',      'Rosa Gómez',  'Bogotá',   'Calle 10 # 20-30, Bogotá',   '3105550101', '$ilp.interledger-test.dev/tienda-rosa',     'demo1234'),
-- ('sanjose@demo.relia',  'Droguería San José',    'Luis Pérez',  'Medellín', 'Carrera 45 # 12-08, Medellín', '3105550102', '$ilp.interledger-test.dev/drogueria-san-jose', 'demo1234'),
-- ('elpaisa@demo.relia',  'Minimercado El Paisa',  'Diana Ríos',  'Cali',     'Avenida 6N # 25-14, Cali',   '3105550103', '$ilp.interledger-test.dev/minimercado-el-paisa', 'demo1234'),
-- ('la70@demo.relia',     'Papelería La 70',       'Jorge Mora',  'Bogotá',   'Carrera 70 # 80-22, Bogotá', '3105550104', '$ilp.interledger-test.dev/papeleria-la-70', 'demo1234');
--
-- -- Usuario de demo con su wallet vinculada. El celular 3000000002 queda sin
-- -- llave a propósito, para probar la invitación por WhatsApp.
-- INSERT INTO llaves_wallet (celular, payment_pointer)
-- VALUES ('3000000001', '$ilp.interledger-test.dev/usuario-demo');
