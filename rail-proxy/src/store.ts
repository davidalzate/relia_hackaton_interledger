import { mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import Database from 'better-sqlite3';

export type Db = Database.Database;

// Cada ticket que necesita tablas agrega su migración al final. Nunca se edita una ya publicada.
const MIGRATIONS: string[] = [
  // 1. Permiso permanente de Relia (grant outgoing-payment interactivo sobre su billetera).
  `CREATE TABLE relia_grants (
    id                      TEXT PRIMARY KEY,
    -- pending: falta la aprobación del operador; approved: vigente; replaced: lo reemplazó otro aprobado;
    -- superseded: llegó otra petición antes de aprobarse; rejected: el operador lo rechazó;
    -- abandoned: venció la ventana de aprobación.
    state                   TEXT NOT NULL
                            CHECK (state IN ('pending', 'approved', 'replaced', 'superseded', 'rejected', 'abandoned')),
    debit_value             TEXT NOT NULL,
    asset_code              TEXT NOT NULL,
    asset_scale             INTEGER NOT NULL,
    interval                TEXT NOT NULL,
    approval_url            TEXT,
    -- Continuación GNAP (secreta): consultar mientras está pendiente y revocar el grant después.
    continue_uri            TEXT NOT NULL,
    continue_token          TEXT NOT NULL,
    next_poll_at            INTEGER,
    interaction_expires_at  INTEGER,
    -- Access token del grant (secreto).
    access_token            TEXT,
    manage_url              TEXT,
    token_expires_at        INTEGER,
    approved_at             TEXT,
    created_at              TEXT NOT NULL,
    updated_at              TEXT NOT NULL
  );
  CREATE INDEX ix_relia_grants_state ON relia_grants (state);`,

  // 2. Pagos de riel.
  `CREATE TABLE payments (
    id                      TEXT PRIMARY KEY,
    idempotency_key         TEXT NOT NULL UNIQUE,
    direction               TEXT NOT NULL CHECK (direction IN ('outgoing', 'incoming')),
    status                  TEXT NOT NULL
                            CHECK (status IN ('pending_approval', 'processing', 'completed', 'failed',
                                              'rejected', 'expired', 'cancelled')),
    sender                  TEXT NOT NULL,
    receiver                TEXT NOT NULL,
    asset_code              TEXT NOT NULL,
    asset_scale             INTEGER NOT NULL,
    receive_value           TEXT NOT NULL,
    debit_value             TEXT,
    received_value          TEXT,
    description             TEXT,
    expires_at              TEXT,
    failure_code            TEXT,
    failure_detail          TEXT,
    -- Recursos en el riel y el servidor de autorización que emite los tokens para leerlos.
    incoming_payment_url    TEXT,
    incoming_auth_server    TEXT,
    quote_url               TEXT,
    outgoing_payment_url    TEXT,
    -- Sincronizador.
    processing_since        INTEGER,
    next_poll_at            INTEGER,
    created_at              TEXT NOT NULL,
    updated_at              TEXT NOT NULL,
    completed_at            TEXT,
    version                 INTEGER NOT NULL DEFAULT 1
  );
  CREATE INDEX ix_payments_poll ON payments (status, next_poll_at);`,

  // 3. Huella del cuerpo de creación (idempotencia). NULL en pagos creados antes de esta migración.
  `ALTER TABLE payments ADD COLUMN request_hash TEXT;`,

  // 4. Permiso de Relia: estado `revoked` (el riel lo revocó) y tope agotado hasta el siguiente periodo.
  //    SQLite no permite cambiar un CHECK: se reconstruye la tabla.
  `CREATE TABLE relia_grants_v4 (
    id                      TEXT PRIMARY KEY,
    state                   TEXT NOT NULL
                            CHECK (state IN ('pending', 'approved', 'replaced', 'superseded', 'rejected', 'abandoned', 'revoked')),
    debit_value             TEXT NOT NULL,
    asset_code              TEXT NOT NULL,
    asset_scale             INTEGER NOT NULL,
    interval                TEXT NOT NULL,
    approval_url            TEXT,
    continue_uri            TEXT NOT NULL,
    continue_token          TEXT NOT NULL,
    next_poll_at            INTEGER,
    interaction_expires_at  INTEGER,
    access_token            TEXT,
    manage_url              TEXT,
    token_expires_at        INTEGER,
    approved_at             TEXT,
    exhausted_until         INTEGER,
    created_at              TEXT NOT NULL,
    updated_at              TEXT NOT NULL
  );
  INSERT INTO relia_grants_v4 (id, state, debit_value, asset_code, asset_scale, interval, approval_url, continue_uri,
    continue_token, next_poll_at, interaction_expires_at, access_token, manage_url, token_expires_at, approved_at,
    created_at, updated_at)
  SELECT id, state, debit_value, asset_code, asset_scale, interval, approval_url, continue_uri, continue_token,
    next_poll_at, interaction_expires_at, access_token, manage_url, token_expires_at, approved_at, created_at, updated_at
  FROM relia_grants;
  DROP TABLE relia_grants;
  ALTER TABLE relia_grants_v4 RENAME TO relia_grants;
  CREATE INDEX ix_relia_grants_state ON relia_grants (state);`,
];

/** Abre (o crea) el archivo SQLite del proxy y lo lleva a la última versión del esquema. */
export function openStore(path: string): Db {
  mkdirSync(dirname(path), { recursive: true });
  const db = new Database(path);
  db.pragma('journal_mode = WAL');
  db.pragma('foreign_keys = ON');
  migrate(db);
  return db;
}

function migrate(db: Db): void {
  const current = db.pragma('user_version', { simple: true }) as number;
  for (let version = current; version < MIGRATIONS.length; version++) {
    db.transaction(() => {
      db.exec(MIGRATIONS[version]!);
      db.pragma(`user_version = ${version + 1}`);
    })();
  }
}
