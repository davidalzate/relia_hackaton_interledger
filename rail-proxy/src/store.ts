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
