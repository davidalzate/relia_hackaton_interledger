-- =====================================================================
-- RELIA (hackathon Interledger) - Esquema PostgreSQL 16
--
-- Corre igual en un Postgres limpio (contenedor local, ver backend/docker-compose.yml)
-- y en Azure. Se aplica sobre el esquema public, que ya existe en ambos.
-- Orden: tipos, tablas, funciones, triggers y vistas.
--
-- Agregados para cumplir contracts/relia-api.openapi.yaml:
--   * idempotency_records: respuesta guardada por Idempotency-Key.
--   * operations.failure_code y operations.cash_handed_over_at.
--   * agents.wallet_simulated: billetera que no existe en el riel (demo).
--   * payments.rail_payment_id y payments.rail_version: id y versión del pago en el proxy.
-- =====================================================================

-- =====================================================================
-- TIPOS
-- =====================================================================

-- DROP TYPE public.event_source;

CREATE TYPE public.event_source AS ENUM (
	'agent',
	'system',
	'rail',
	'simulator');

-- DROP TYPE public.ledger_entry_type;

CREATE TYPE public.ledger_entry_type AS ENUM (
	'deposit',
	'withdrawal',
	'float_load',
	'settlement',
	'cash_adjustment');

-- DROP TYPE public.load_method;

CREATE TYPE public.load_method AS ENUM (
	'bre_b',
	'wallet');

-- DROP TYPE public.message_channel;

CREATE TYPE public.message_channel AS ENUM (
	'sms',
	'whatsapp');

-- DROP TYPE public.message_type;

CREATE TYPE public.message_type AS ENUM (
	'otp',
	'wallet_invitation',
	'deposit_notice',
	'payment_request',
	'receipt',
	'commission_notice');

-- DROP TYPE public.operation_status;

CREATE TYPE public.operation_status AS ENUM (
	'pending',
	'completed',
	'failed',
	'expired',
	'cancelled');

-- DROP TYPE public.operation_type;

CREATE TYPE public.operation_type AS ENUM (
	'deposit',
	'withdrawal',
	'float_load');

-- DROP TYPE public.otp_purpose;

CREATE TYPE public.otp_purpose AS ENUM (
	'wallet_linking',
	'receipt_lookup');

-- DROP TYPE public.payment_direction;

CREATE TYPE public.payment_direction AS ENUM (
	'incoming',
	'outgoing');

-- DROP TYPE public.payment_rail;

CREATE TYPE public.payment_rail AS ENUM (
	'open_payments',
	'bre_b_simulated');

-- DROP TYPE public.receipt_issue_channel;

CREATE TYPE public.receipt_issue_channel AS ENUM (
	'screen',
	'pdf',
	'print',
	'whatsapp',
	'sms');

-- DROP TYPE public.receipt_requester;

CREATE TYPE public.receipt_requester AS ENUM (
	'agent',
	'user',
	'system');

-- DROP TYPE public.settlement_status;

CREATE TYPE public.settlement_status AS ENUM (
	'pending',
	'paid',
	'voided');

-- =====================================================================
-- TABLAS
-- =====================================================================

-- public.admins definition

-- Drop table

-- DROP TABLE public.admins;

CREATE TABLE public.admins (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	email text NOT NULL,
	"name" text NOT NULL,
	"password" text NOT NULL,
	active bool DEFAULT true NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT admins_email_check CHECK ((email ~ '^[^@[:space:]]+@[^@[:space:]]+$'::text)),
	CONSTRAINT admins_name_check CHECK ((length(btrim(name)) > 0)),
	CONSTRAINT admins_password_check CHECK ((length(password) >= 4)),
	CONSTRAINT admins_pkey PRIMARY KEY (id)
);
CREATE UNIQUE INDEX ux_admins_email ON public.admins USING btree (lower(email));
COMMENT ON TABLE public.admins IS 'Administradores de Relia. Acceden al panel y pagan las comisiones.';

-- public.agents definition

-- Drop table

-- DROP TABLE public.agents;

CREATE TABLE public.agents (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	email text NOT NULL,
	"name" text NOT NULL,
	contact_person text NOT NULL,
	city text NOT NULL,
	address text NOT NULL,
	phone text NOT NULL,
	payment_pointer text NOT NULL,
	"password" text NOT NULL,
	active bool DEFAULT true NOT NULL,
	wallet_simulated bool DEFAULT false NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT agents_city_check CHECK ((length(btrim(city)) > 0)),
	CONSTRAINT agents_contact_person_check CHECK ((length(btrim(contact_person)) > 0)),
	CONSTRAINT agents_email_check CHECK ((email ~ '^[^@[:space:]]+@[^@[:space:]]+$'::text)),
	CONSTRAINT agents_name_check CHECK ((length(btrim(name)) > 0)),
	CONSTRAINT agents_password_check CHECK ((length(password) >= 4)),
	CONSTRAINT agents_payment_pointer_check CHECK ((payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT agents_phone_check CHECK ((phone ~ '^3[0-9]{9}$'::text)),
	CONSTRAINT agents_pkey PRIMARY KEY (id)
);
CREATE UNIQUE INDEX ux_agents_email ON public.agents USING btree (lower(email));
CREATE UNIQUE INDEX ux_agents_phone ON public.agents USING btree (phone);
CREATE UNIQUE INDEX ux_agents_wallet ON public.agents USING btree (payment_pointer);
COMMENT ON TABLE public.agents IS 'Comercios que operan efectivo con Relia. Incluye su acceso y su wallet.';
COMMENT ON COLUMN public.agents.wallet_simulated IS 'Solo demo: la billetera no existe en el riel; su liquidación se paga sin riel.';

-- public.fees definition

-- Drop table

-- DROP TABLE public.fees;

CREATE TABLE public.fees (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	total_commission int8 NOT NULL,
	relia_commission int8 NOT NULL,
	agent_commission int8 NOT NULL,
	valid_from timestamptz DEFAULT now() NOT NULL,
	valid_until timestamptz NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_fees_split CHECK ((total_commission = (relia_commission + agent_commission))),
	CONSTRAINT ck_fees_validity CHECK (((valid_until IS NULL) OR (valid_until > valid_from))),
	CONSTRAINT fees_agent_commission_check CHECK ((agent_commission >= 0)),
	CONSTRAINT fees_pkey PRIMARY KEY (id),
	CONSTRAINT fees_relia_commission_check CHECK ((relia_commission >= 0)),
	CONSTRAINT fees_total_commission_check CHECK ((total_commission > 0))
);
CREATE UNIQUE INDEX ux_fees_current ON public.fees USING btree ((true)) WHERE (valid_until IS NULL);
COMMENT ON TABLE public.fees IS 'Tarifa por operación, versionada. Cada operación copia sus valores.';

-- public.otp_challenges definition

-- Drop table

-- DROP TABLE public.otp_challenges;

CREATE TABLE public.otp_challenges (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	mobile text NOT NULL,
	purpose public.otp_purpose DEFAULT 'wallet_linking'::otp_purpose NOT NULL,
	payment_pointer text NULL,
	code_hash text NOT NULL,
	attempts int2 DEFAULT 0 NOT NULL,
	max_attempts int2 DEFAULT 5 NOT NULL,
	expires_at timestamptz NOT NULL,
	verified_at timestamptz NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_otp_attempts CHECK ((attempts <= max_attempts)),
	CONSTRAINT ck_otp_expiration CHECK ((expires_at > created_at)),
	CONSTRAINT ck_otp_purpose CHECK (((purpose = 'wallet_linking'::otp_purpose) = (payment_pointer IS NOT NULL))),
	CONSTRAINT otp_challenges_attempts_check CHECK ((attempts >= 0)),
	CONSTRAINT otp_challenges_max_attempts_check CHECK ((max_attempts > 0)),
	CONSTRAINT otp_challenges_mobile_check CHECK ((mobile ~ '^3[0-9]{9}$'::text)),
	CONSTRAINT otp_challenges_payment_pointer_check CHECK ((payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT otp_challenges_pkey PRIMARY KEY (id)
);
CREATE INDEX ix_otp_mobile ON public.otp_challenges USING btree (mobile, created_at DESC);
COMMENT ON TABLE public.otp_challenges IS 'OTP de vinculación de wallet y de consulta de recibos. Código guardado como hash, con expiración e intentos.';

-- public.wallet_keys definition

-- Drop table

-- DROP TABLE public.wallet_keys;

CREATE TABLE public.wallet_keys (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	mobile text NOT NULL,
	payment_pointer text NOT NULL,
	wallet_address_url text GENERATED ALWAYS AS ('https://'::text || substr(payment_pointer, 2)) STORED NULL,
	linked_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ux_wallet_keys_mobile UNIQUE (mobile),
	CONSTRAINT ux_wallet_keys_wallet UNIQUE (wallet_address_url),
	CONSTRAINT wallet_keys_mobile_check CHECK ((mobile ~ '^3[0-9]{9}$'::text)),
	CONSTRAINT wallet_keys_payment_pointer_check CHECK ((payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT wallet_keys_pkey PRIMARY KEY (id)
);
COMMENT ON TABLE public.wallet_keys IS 'Celular vinculado a un payment pointer. Inmutable. Borrar solo con TRUNCATE en el reinicio del demo.';

-- public.agent_balances definition

-- Drop table

-- DROP TABLE public.agent_balances;

CREATE TABLE public.agent_balances (
	agent_id uuid NOT NULL,
	float_balance int8 DEFAULT 0 NOT NULL,
	accrued_commission int8 DEFAULT 0 NOT NULL,
	settled_commission_total int8 DEFAULT 0 NOT NULL,
	expected_cash int8 DEFAULT 0 NOT NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT agent_balances_accrued_commission_check CHECK ((accrued_commission >= 0)),
	CONSTRAINT agent_balances_expected_cash_check CHECK ((expected_cash >= 0)),
	CONSTRAINT agent_balances_float_balance_check CHECK ((float_balance >= 0)),
	CONSTRAINT agent_balances_pkey PRIMARY KEY (agent_id),
	CONSTRAINT agent_balances_settled_commission_total_check CHECK ((settled_commission_total >= 0)),
	CONSTRAINT agent_balances_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.agents(id) ON DELETE RESTRICT
);
COMMENT ON TABLE public.agent_balances IS 'Totales de cada corresponsal: cupo, comisión y efectivo esperado. Una fila por corresponsal.';

-- public.operations definition

-- Drop table

-- DROP TABLE public.operations;

CREATE TABLE public.operations (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	"number" int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	display_number text GENERATED ALWAYS AS ('OP-'::text || lpad(number::text, 6, '0'::text)) STORED NULL,
	agent_id uuid NOT NULL,
	"type" public.operation_type NOT NULL,
	load_method public.load_method NULL,
	wallet_key_id uuid NULL,
	requested_amount int8 NOT NULL,
	round_value bool DEFAULT false NOT NULL,
	fee_id int8 NULL,
	total_commission int8 DEFAULT 0 NOT NULL,
	relia_commission int8 DEFAULT 0 NOT NULL,
	agent_commission int8 DEFAULT 0 NOT NULL,
	net_amount int8 NOT NULL,
	charged_amount int8 NOT NULL,
	status public.operation_status DEFAULT 'pending'::operation_status NOT NULL,
	failure_code text NULL,
	idempotency_key text NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	completed_at timestamptz NULL,
	cash_handed_over_at timestamptz NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_op_completed CHECK (((status = 'completed'::operation_status) = (completed_at IS NOT NULL))),
	CONSTRAINT ck_op_failure_code CHECK (((failure_code IS NULL) OR ((failure_code = ANY (ARRAY['customer_rejected'::text, 'request_expired'::text, 'request_cancelled'::text, 'payment_failed'::text, 'rail_unavailable'::text])) AND (status = ANY (ARRAY['failed'::operation_status, 'expired'::operation_status, 'cancelled'::operation_status]))))),
	CONSTRAINT ck_op_cash_handover CHECK (((cash_handed_over_at IS NULL) OR ((type = 'withdrawal'::operation_type) AND (status = 'completed'::operation_status)))),
	CONSTRAINT ck_op_deposit_withdrawal CHECK (((type = 'float_load'::operation_type) OR ((load_method IS NULL) AND (wallet_key_id IS NOT NULL) AND (fee_id IS NOT NULL) AND (requested_amount > total_commission) AND (total_commission = (relia_commission + agent_commission)) AND (charged_amount = (net_amount + total_commission)) AND ((round_value AND (net_amount = requested_amount)) OR ((NOT round_value) AND (net_amount = (requested_amount - total_commission))))))),
	CONSTRAINT ck_op_float_load CHECK (((type <> 'float_load'::operation_type) OR ((load_method IS NOT NULL) AND (wallet_key_id IS NULL) AND (fee_id IS NULL) AND (NOT round_value) AND (total_commission = 0) AND (relia_commission = 0) AND (agent_commission = 0) AND (net_amount = requested_amount) AND (charged_amount = requested_amount)))),
	CONSTRAINT operations_agent_commission_check CHECK ((agent_commission >= 0)),
	CONSTRAINT operations_charged_amount_check CHECK ((charged_amount > 0)),
	CONSTRAINT operations_net_amount_check CHECK ((net_amount > 0)),
	CONSTRAINT operations_pkey PRIMARY KEY (id),
	CONSTRAINT operations_relia_commission_check CHECK ((relia_commission >= 0)),
	CONSTRAINT operations_requested_amount_check CHECK ((requested_amount > 0)),
	CONSTRAINT operations_total_commission_check CHECK ((total_commission >= 0)),
	CONSTRAINT ux_operations_idempotency UNIQUE (agent_id, idempotency_key),
	CONSTRAINT operations_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.agents(id) ON DELETE RESTRICT,
	CONSTRAINT operations_fee_id_fkey FOREIGN KEY (fee_id) REFERENCES public.fees(id) ON DELETE RESTRICT,
	CONSTRAINT operations_wallet_key_id_fkey FOREIGN KEY (wallet_key_id) REFERENCES public.wallet_keys(id) ON DELETE RESTRICT
);
CREATE INDEX ix_operations_agent ON public.operations USING btree (agent_id, created_at DESC);
CREATE INDEX ix_operations_pending ON public.operations USING btree (created_at) WHERE (status = 'pending'::operation_status);
COMMENT ON TABLE public.operations IS 'Depósitos, retiros y cargas de cupo. Copia la tarifa y los montos calculados. Sus datos no cambian después de creada.';
COMMENT ON COLUMN public.operations.failure_code IS 'Motivo de una operación failed, expired o cancelled (FailureCode del contrato).';
COMMENT ON COLUMN public.operations.cash_handed_over_at IS 'Solo retiros completados: cuándo el corresponsal confirmó la entrega del efectivo. Se fija una vez.';

-- public.receipts definition

-- Drop table

-- DROP TABLE public.receipts;

CREATE TABLE public.receipts (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	operation_id uuid NOT NULL,
	code text DEFAULT upper(substr(replace(gen_random_uuid()::text, '-'::text, ''::text), 1, 12)) NOT NULL,
	"data" jsonb NOT NULL,
	content_hash text NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT receipts_code_check CHECK ((code ~ '^[0-9A-F]{12}$'::text)),
	CONSTRAINT receipts_code_key UNIQUE (code),
	CONSTRAINT receipts_content_hash_check CHECK ((content_hash ~ '^[0-9a-f]{64}$'::text)),
	CONSTRAINT receipts_operation_id_key UNIQUE (operation_id),
	CONSTRAINT receipts_pkey PRIMARY KEY (id),
	CONSTRAINT receipts_operation_id_fkey FOREIGN KEY (operation_id) REFERENCES public.operations(id) ON DELETE RESTRICT
);
COMMENT ON TABLE public.receipts IS 'Contenido congelado del recibo de cada depósito o retiro completado. Solo inserción.';

-- public.settlements definition

-- Drop table

-- DROP TABLE public.settlements;

CREATE TABLE public.settlements (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	"number" int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	display_number text GENERATED ALWAYS AS ('LQ-'::text || lpad(number::text, 6, '0'::text)) STORED NULL,
	agent_id uuid NOT NULL,
	created_by_admin_id uuid NOT NULL,
	destination_payment_pointer text NOT NULL,
	period_start date NOT NULL,
	period_end date NOT NULL,
	amount int8 NOT NULL,
	status public.settlement_status DEFAULT 'pending'::settlement_status NOT NULL,
	paid_at timestamptz NULL,
	void_reason text NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_settlement_paid CHECK (((status = 'paid'::settlement_status) = (paid_at IS NOT NULL))),
	CONSTRAINT ck_settlement_period CHECK ((period_end >= period_start)),
	CONSTRAINT ck_settlement_voided CHECK (((status = 'voided'::settlement_status) = (void_reason IS NOT NULL))),
	CONSTRAINT settlements_amount_check CHECK ((amount > 0)),
	CONSTRAINT settlements_destination_payment_pointer_check CHECK ((destination_payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT settlements_pkey PRIMARY KEY (id),
	CONSTRAINT settlements_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.agents(id) ON DELETE RESTRICT,
	CONSTRAINT settlements_created_by_admin_id_fkey FOREIGN KEY (created_by_admin_id) REFERENCES public.admins(id) ON DELETE RESTRICT
);
CREATE INDEX ix_settlements_agent ON public.settlements USING btree (agent_id, created_at DESC);
CREATE UNIQUE INDEX ux_settlements_one_pending ON public.settlements USING btree (agent_id) WHERE (status = 'pending'::settlement_status);
COMMENT ON TABLE public.settlements IS 'Pago manual mensual de la comisión total pendiente, a la wallet del corresponsal.';

-- public.simulated_messages definition

-- Drop table

-- DROP TABLE public.simulated_messages;

CREATE TABLE public.simulated_messages (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	channel public.message_channel NOT NULL,
	"type" public.message_type NOT NULL,
	recipient text NOT NULL,
	"content" text NOT NULL,
	link text NULL,
	operation_id uuid NULL,
	is_read bool DEFAULT false NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT simulated_messages_pkey PRIMARY KEY (id),
	CONSTRAINT simulated_messages_recipient_check CHECK ((recipient ~ '^3[0-9]{9}$'::text)),
	CONSTRAINT simulated_messages_operation_id_fkey FOREIGN KEY (operation_id) REFERENCES public.operations(id) ON DELETE SET NULL
);
CREATE INDEX ix_messages_recipient ON public.simulated_messages USING btree (recipient, created_at DESC);
COMMENT ON TABLE public.simulated_messages IS 'Bandeja simulada de SMS y WhatsApp. Solo existe en el demo.';

-- public.operation_events definition

-- Drop table

-- DROP TABLE public.operation_events;

CREATE TABLE public.operation_events (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	operation_id uuid NOT NULL,
	previous_status public.operation_status NULL,
	new_status public.operation_status NOT NULL,
	"source" public.event_source NOT NULL,
	details text NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT operation_events_pkey PRIMARY KEY (id),
	CONSTRAINT operation_events_operation_id_fkey FOREIGN KEY (operation_id) REFERENCES public.operations(id) ON DELETE RESTRICT
);
CREATE INDEX ix_operation_events ON public.operation_events USING btree (operation_id, id);
COMMENT ON TABLE public.operation_events IS 'Bitácora de solo inserción con cada cambio de estado de una operación.';

-- public.payments definition

-- Drop table

-- DROP TABLE public.payments;

CREATE TABLE public.payments (
	id uuid DEFAULT gen_random_uuid() NOT NULL,
	operation_id uuid NULL,
	settlement_id uuid NULL,
	rail public.payment_rail NOT NULL,
	direction public.payment_direction NOT NULL,
	status public.operation_status DEFAULT 'pending'::operation_status NOT NULL,
	amount int8 NOT NULL,
	currency bpchar(3) DEFAULT 'COP'::bpchar NOT NULL,
	source_payment_pointer text NOT NULL,
	destination_payment_pointer text NOT NULL,
	op_incoming_payment_url text NULL,
	op_quote_url text NULL,
	op_outgoing_payment_url text NULL,
	approval_url text NULL,
	grant_continue_uri text NULL,
	grant_continue_token_encrypted bytea NULL,
	rail_reference text NULL,
	rail_payment_id uuid NULL,
	rail_version int4 NULL,
	debit_value text NULL,
	debit_asset_code text NULL,
	debit_asset_scale int2 NULL,
	received_value text NULL,
	received_asset_code text NULL,
	received_asset_scale int2 NULL,
	quoted_receive_value text NULL,
	quoted_receive_asset_code text NULL,
	quoted_receive_asset_scale int2 NULL,
	expires_at timestamptz NULL,
	error_code text NULL,
	error_message text NULL,
	rail_details jsonb DEFAULT '{}'::jsonb NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	completed_at timestamptz NULL,
	updated_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_payments_completed CHECK (((status = 'completed'::operation_status) = (completed_at IS NOT NULL))),
	CONSTRAINT ck_payments_debit_complete CHECK ((((debit_value IS NULL) = (debit_asset_code IS NULL)) AND ((debit_value IS NULL) = (debit_asset_scale IS NULL)))),
	CONSTRAINT ck_payments_distinct_wallets CHECK ((source_payment_pointer <> destination_payment_pointer)),
	CONSTRAINT ck_payments_quoted_receive_complete CHECK ((((quoted_receive_value IS NULL) = (quoted_receive_asset_code IS NULL)) AND ((quoted_receive_value IS NULL) = (quoted_receive_asset_scale IS NULL)))),
	CONSTRAINT ck_payments_parent CHECK (((operation_id IS NULL) <> (settlement_id IS NULL))),
	CONSTRAINT ck_payments_received_complete CHECK ((((received_value IS NULL) = (received_asset_code IS NULL)) AND ((received_value IS NULL) = (received_asset_scale IS NULL)))),
	CONSTRAINT ck_payments_rail_version CHECK (((rail_version IS NULL) OR (rail_version >= 1))),
	CONSTRAINT payments_amount_check CHECK ((amount > 0)),
	CONSTRAINT payments_currency_check CHECK ((currency = 'COP'::bpchar)),
	CONSTRAINT payments_destination_payment_pointer_check CHECK ((destination_payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT payments_pkey PRIMARY KEY (id),
	CONSTRAINT payments_source_payment_pointer_check CHECK ((source_payment_pointer ~ '^\$[^[:space:]/]+(/[^[:space:]]*)?$'::text)),
	CONSTRAINT payments_operation_id_fkey FOREIGN KEY (operation_id) REFERENCES public.operations(id) ON DELETE RESTRICT,
	CONSTRAINT payments_settlement_id_fkey FOREIGN KEY (settlement_id) REFERENCES public.settlements(id) ON DELETE RESTRICT
);
CREATE INDEX ix_payments_destination ON public.payments USING btree (destination_payment_pointer, created_at DESC);
CREATE INDEX ix_payments_expiry ON public.payments USING btree (expires_at) WHERE (status = 'pending'::operation_status);
CREATE INDEX ix_payments_operation ON public.payments USING btree (operation_id) WHERE (operation_id IS NOT NULL);
CREATE INDEX ix_payments_settlement ON public.payments USING btree (settlement_id) WHERE (settlement_id IS NOT NULL);
CREATE INDEX ix_payments_source ON public.payments USING btree (source_payment_pointer, created_at DESC);
CREATE UNIQUE INDEX ux_payments_incoming_url ON public.payments USING btree (op_incoming_payment_url) WHERE (op_incoming_payment_url IS NOT NULL);
CREATE UNIQUE INDEX ux_payments_outgoing_url ON public.payments USING btree (op_outgoing_payment_url) WHERE (op_outgoing_payment_url IS NOT NULL);
CREATE UNIQUE INDEX ux_payments_rail_payment_id ON public.payments USING btree (rail_payment_id) WHERE (rail_payment_id IS NOT NULL);
COMMENT ON TABLE public.payments IS 'Intentos de pago por riel (Open Payments o Bre-B simulado) de operaciones y liquidaciones.';
COMMENT ON COLUMN public.payments.rail_payment_id IS 'Id del pago en el proxy de riel (railPaymentId). Null si no pasó por el proxy.';
COMMENT ON COLUMN public.payments.rail_version IS 'Último version del pago en el proxy ya procesado; lecturas con versión menor o igual se descartan.';
COMMENT ON COLUMN public.payments.quoted_receive_value IS 'Lo que el quote del riel prometió entregar al receptor, en unidades mínimas de su moneda (quoted_receive_asset_code y _scale). Null si aún no hay quote.';

-- public.receipt_issuances definition

-- Drop table

-- DROP TABLE public.receipt_issuances;

CREATE TABLE public.receipt_issuances (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	receipt_id uuid NOT NULL,
	channel public.receipt_issue_channel NOT NULL,
	requested_by public.receipt_requester NOT NULL,
	agent_id uuid NULL,
	user_mobile text NULL,
	is_copy bool DEFAULT false NOT NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_issuance_requester CHECK ((((requested_by = 'agent'::receipt_requester) AND (agent_id IS NOT NULL)) OR ((requested_by = 'user'::receipt_requester) AND (user_mobile IS NOT NULL)) OR (requested_by = 'system'::receipt_requester))),
	CONSTRAINT receipt_issuances_pkey PRIMARY KEY (id),
	CONSTRAINT receipt_issuances_user_mobile_check CHECK ((user_mobile ~ '^3[0-9]{9}$'::text)),
	CONSTRAINT receipt_issuances_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.agents(id) ON DELETE RESTRICT,
	CONSTRAINT receipt_issuances_receipt_id_fkey FOREIGN KEY (receipt_id) REFERENCES public.receipts(id) ON DELETE RESTRICT
);
CREATE INDEX ix_receipt_issuances ON public.receipt_issuances USING btree (receipt_id, id);
COMMENT ON TABLE public.receipt_issuances IS 'Registro de cada vez que un recibo se muestra, descarga o envía. Desde la segunda es copia.';

-- public.ledger_entries definition

-- Drop table

-- DROP TABLE public.ledger_entries;

CREATE TABLE public.ledger_entries (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	agent_id uuid NOT NULL,
	"type" public.ledger_entry_type NOT NULL,
	operation_id uuid NULL,
	settlement_id uuid NULL,
	payment_id uuid NULL,
	float_before int8 NOT NULL,
	float_change int8 NOT NULL,
	float_after int8 NOT NULL,
	commission_before int8 NOT NULL,
	commission_change int8 NOT NULL,
	commission_after int8 NOT NULL,
	cash_before int8 NOT NULL,
	cash_change int8 NOT NULL,
	cash_after int8 NOT NULL,
	reason text NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_ledger_cash CHECK ((cash_after = (cash_before + cash_change))),
	CONSTRAINT ck_ledger_commission CHECK ((commission_after = (commission_before + commission_change))),
	CONSTRAINT ck_ledger_float CHECK ((float_after = (float_before + float_change))),
	CONSTRAINT ck_ledger_origin CHECK ((((type = 'settlement'::ledger_entry_type) AND (settlement_id IS NOT NULL) AND (operation_id IS NULL) AND (payment_id IS NOT NULL) AND (reason IS NULL)) OR ((type = ANY (ARRAY['deposit'::ledger_entry_type, 'withdrawal'::ledger_entry_type, 'float_load'::ledger_entry_type])) AND (operation_id IS NOT NULL) AND (settlement_id IS NULL) AND (payment_id IS NOT NULL) AND (reason IS NULL)) OR ((type = 'cash_adjustment'::ledger_entry_type) AND (operation_id IS NULL) AND (settlement_id IS NULL) AND (payment_id IS NULL) AND (length(btrim(COALESCE(reason, ''::text))) > 0)))),
	CONSTRAINT ck_ledger_signs CHECK ((((type = 'deposit'::ledger_entry_type) AND (float_change < 0) AND (commission_change > 0) AND (cash_change = (- float_change))) OR ((type = 'withdrawal'::ledger_entry_type) AND (float_change > 0) AND (commission_change > 0) AND (cash_change = (- float_change))) OR ((type = 'float_load'::ledger_entry_type) AND (float_change > 0) AND (commission_change = 0) AND (cash_change = 0)) OR ((type = 'settlement'::ledger_entry_type) AND (float_change = 0) AND (commission_change < 0) AND (cash_change = 0)) OR ((type = 'cash_adjustment'::ledger_entry_type) AND (float_change = 0) AND (commission_change = 0) AND (cash_change <> 0)))),
	CONSTRAINT ledger_entries_cash_after_check CHECK ((cash_after >= 0)),
	CONSTRAINT ledger_entries_cash_before_check CHECK ((cash_before >= 0)),
	CONSTRAINT ledger_entries_commission_after_check CHECK ((commission_after >= 0)),
	CONSTRAINT ledger_entries_commission_before_check CHECK ((commission_before >= 0)),
	CONSTRAINT ledger_entries_float_after_check CHECK ((float_after >= 0)),
	CONSTRAINT ledger_entries_float_before_check CHECK ((float_before >= 0)),
	CONSTRAINT ledger_entries_pkey PRIMARY KEY (id),
	CONSTRAINT ledger_entries_agent_id_fkey FOREIGN KEY (agent_id) REFERENCES public.agents(id) ON DELETE RESTRICT,
	CONSTRAINT ledger_entries_operation_id_fkey FOREIGN KEY (operation_id) REFERENCES public.operations(id) ON DELETE RESTRICT,
	CONSTRAINT ledger_entries_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.payments(id) ON DELETE RESTRICT,
	CONSTRAINT ledger_entries_settlement_id_fkey FOREIGN KEY (settlement_id) REFERENCES public.settlements(id) ON DELETE RESTRICT
);
CREATE INDEX ix_ledger_agent ON public.ledger_entries USING btree (agent_id, id DESC);
CREATE UNIQUE INDEX ux_ledger_operation ON public.ledger_entries USING btree (operation_id) WHERE (operation_id IS NOT NULL);
CREATE UNIQUE INDEX ux_ledger_payment ON public.ledger_entries USING btree (payment_id);
CREATE UNIQUE INDEX ux_ledger_settlement ON public.ledger_entries USING btree (settlement_id) WHERE (settlement_id IS NOT NULL);
COMMENT ON TABLE public.ledger_entries IS 'Libro de solo inserción del cupo, la comisión y el efectivo: antes, variación y después.';

-- public.payment_events definition

-- Drop table

-- DROP TABLE public.payment_events;

CREATE TABLE public.payment_events (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	payment_id uuid NOT NULL,
	"action" text NOT NULL,
	previous_status public.operation_status NULL,
	new_status public.operation_status NOT NULL,
	"source" public.event_source NOT NULL,
	http_status int2 NULL,
	rail_response jsonb NULL,
	details text NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT payment_events_pkey PRIMARY KEY (id),
	CONSTRAINT payment_events_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.payments(id) ON DELETE RESTRICT
);
CREATE INDEX ix_payment_events ON public.payment_events USING btree (payment_id, id);
COMMENT ON TABLE public.payment_events IS 'Bitácora de solo inserción de cada pago: llamadas al riel, respuestas y cambios de estado.';

-- public.idempotency_records definition

CREATE TABLE public.idempotency_records (
	id int8 GENERATED ALWAYS AS IDENTITY( INCREMENT BY 1 MINVALUE 1 MAXVALUE 9223372036854775807 START 1 CACHE 1 NO CYCLE) NOT NULL,
	"scope" text NOT NULL,
	idempotency_key text NOT NULL,
	request_hash text NOT NULL,
	response_status int2 NOT NULL,
	response_body jsonb NOT NULL,
	resource_id text NULL,
	created_at timestamptz DEFAULT now() NOT NULL,
	CONSTRAINT ck_idempotency_key_length CHECK (((length(idempotency_key) >= 8) AND (length(idempotency_key) <= 128))),
	CONSTRAINT ck_idempotency_request_hash CHECK ((request_hash ~ '^[0-9a-f]{64}$'::text)),
	CONSTRAINT ck_idempotency_response_status CHECK (((response_status >= 200) AND (response_status < 300))),
	CONSTRAINT idempotency_records_pkey PRIMARY KEY (id),
	CONSTRAINT ux_idempotency_scope_key UNIQUE (scope, idempotency_key)
);
COMMENT ON TABLE public.idempotency_records IS 'Respuesta exitosa de cada creación por Idempotency-Key. scope = ruta + actor; request_hash = SHA-256 del cuerpo. Los errores no se guardan.';

-- =====================================================================
-- FUNCIONES (después de las tablas: issue_receipt devuelve el tipo de fila receipts)
-- =====================================================================

-- DROP FUNCTION public.block_modification();

CREATE OR REPLACE FUNCTION public.block_modification()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    RAISE EXCEPTION 'La tabla % es de solo inserción: no se permite % de filas',
        TG_TABLE_NAME, TG_OP;
END;
$function$
;

-- DROP FUNCTION public.create_initial_balance();

CREATE OR REPLACE FUNCTION public.create_initial_balance()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    INSERT INTO agent_balances (agent_id) VALUES (NEW.id);
    RETURN NEW;
END;
$function$
;

-- DROP FUNCTION public.issue_receipt(uuid);

CREATE OR REPLACE FUNCTION public.issue_receipt(p_operation_id uuid)
 RETURNS receipts
 LANGUAGE plpgsql
AS $function$
DECLARE
    o operations%ROWTYPE;
    c agents%ROWTYPE;
    w wallet_keys%ROWTYPE;
    p payments%ROWTYPE;
    wallet_amount jsonb;
    content jsonb;
    r receipts%ROWTYPE;
BEGIN
    SELECT * INTO o FROM operations WHERE id = p_operation_id;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'No existe la operación %', p_operation_id;
    END IF;
    IF o.status <> 'completed' THEN
        RAISE EXCEPTION 'La operación % no está completada: no tiene recibo', o.display_number;
    END IF;
    IF o.type = 'float_load' THEN
        RAISE EXCEPTION 'La carga de cupo (%) no genera recibo para el usuario', o.display_number;
    END IF;

    SELECT * INTO c FROM agents WHERE id = o.agent_id;
    SELECT * INTO w FROM wallet_keys WHERE id = o.wallet_key_id;
    SELECT * INTO p FROM payments
    WHERE operation_id = o.id AND status = 'completed'
    ORDER BY completed_at DESC, created_at DESC LIMIT 1;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'La operación % no tiene un pago completado', o.display_number;
    END IF;

    -- Lo que entró (depósito) o salió (retiro) de la billetera del usuario, solo si no es COP. Va en unidades
    -- mínimas con su escala, como lo dio el riel. En el depósito, si el riel aún no informó lo recibido, se usa lo
    -- cotizado (quoted_receive_*). Misma regla que Mapping.WalletAmount (backend/API/Services/Mapping.cs):
    -- si cambia una, cambiar la otra.
    IF o.type = 'deposit' AND p.received_asset_code IS DISTINCT FROM 'COP' AND p.received_value IS NOT NULL THEN
        wallet_amount := jsonb_build_object(
            'value', p.received_value, 'asset_code', p.received_asset_code, 'asset_scale', p.received_asset_scale);
    ELSIF o.type = 'deposit' AND p.quoted_receive_asset_code IS DISTINCT FROM 'COP' AND p.quoted_receive_value IS NOT NULL THEN
        wallet_amount := jsonb_build_object(
            'value', p.quoted_receive_value, 'asset_code', p.quoted_receive_asset_code,
            'asset_scale', p.quoted_receive_asset_scale);
    ELSIF o.type = 'withdrawal' AND p.debit_asset_code IS DISTINCT FROM 'COP' AND p.debit_value IS NOT NULL THEN
        wallet_amount := jsonb_build_object(
            'value', p.debit_value, 'asset_code', p.debit_asset_code, 'asset_scale', p.debit_asset_scale);
    END IF;

    content := jsonb_build_object(
        'number', o.display_number,
        'type', o.type,
        'date', to_char(o.completed_at AT TIME ZONE 'America/Bogota', 'YYYY-MM-DD"T"HH24:MI:SS'),
        'currency', 'COP',
        'merchant', jsonb_build_object(
            'name', c.name, 'address', c.address, 'phone', c.phone),
        'user', jsonb_build_object(
            'mobile', w.mobile,
            'payment_pointer', w.payment_pointer),
        'amounts', jsonb_build_object(
            'requested_amount', o.requested_amount,
            'round_value', o.round_value,
            'commission', o.total_commission,
            'net_amount', o.net_amount,
            'charged_amount', o.charged_amount),
        'payment', jsonb_build_object(
            'rail', p.rail,
            'reference', COALESCE(p.op_outgoing_payment_url, p.op_incoming_payment_url, p.rail_reference),
            'wallet_amount', wallet_amount)
    );

    INSERT INTO receipts (operation_id, data, content_hash)
    VALUES (o.id, content, encode(sha256(convert_to(content::text, 'UTF8')), 'hex'))
    RETURNING * INTO r;
    RETURN r;
END;
$function$
;

-- DROP FUNCTION public.issue_receipt_on_completion();

CREATE OR REPLACE FUNCTION public.issue_receipt_on_completion()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF NEW.status = 'completed' AND OLD.status <> 'completed'
       AND NEW.type IN ('deposit', 'withdrawal') THEN
        PERFORM issue_receipt(NEW.id);
    END IF;
    RETURN NULL;
END;
$function$
;

-- DROP FUNCTION public.mark_receipt_copy();

CREATE OR REPLACE FUNCTION public.mark_receipt_copy()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    NEW.is_copy := EXISTS (SELECT 1 FROM receipt_issuances WHERE receipt_id = NEW.receipt_id);
    RETURN NEW;
END;
$function$
;

-- DROP FUNCTION public.protect_operation();

CREATE OR REPLACE FUNCTION public.protect_operation()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'Las operaciones no se pueden borrar (%)', OLD.display_number;
    END IF;

    IF NEW.id IS DISTINCT FROM OLD.id
       OR NEW.number IS DISTINCT FROM OLD.number
       OR NEW.agent_id IS DISTINCT FROM OLD.agent_id
       OR NEW.type IS DISTINCT FROM OLD.type
       OR NEW.load_method IS DISTINCT FROM OLD.load_method
       OR NEW.wallet_key_id IS DISTINCT FROM OLD.wallet_key_id
       OR NEW.requested_amount IS DISTINCT FROM OLD.requested_amount
       OR NEW.round_value IS DISTINCT FROM OLD.round_value
       OR NEW.fee_id IS DISTINCT FROM OLD.fee_id
       OR NEW.total_commission IS DISTINCT FROM OLD.total_commission
       OR NEW.relia_commission IS DISTINCT FROM OLD.relia_commission
       OR NEW.agent_commission IS DISTINCT FROM OLD.agent_commission
       OR NEW.net_amount IS DISTINCT FROM OLD.net_amount
       OR NEW.charged_amount IS DISTINCT FROM OLD.charged_amount
       OR NEW.idempotency_key IS DISTINCT FROM OLD.idempotency_key
       OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
        RAISE EXCEPTION 'La operación % no se puede modificar: sus datos quedan fijos al crearla',
            OLD.display_number;
    END IF;

    IF OLD.status <> 'pending'
       AND (NEW.status <> OLD.status
            OR NEW.completed_at IS DISTINCT FROM OLD.completed_at) THEN
        RAISE EXCEPTION 'La operación % ya está en estado final (%) y no puede cambiar',
            OLD.display_number, OLD.status;
    END IF;

    -- El motivo del fallo queda fijo con el estado final.
    IF OLD.status <> 'pending'
       AND NEW.failure_code IS DISTINCT FROM OLD.failure_code THEN
        RAISE EXCEPTION 'La operación % ya está en estado final: su motivo no puede cambiar',
            OLD.display_number;
    END IF;

    -- La entrega del efectivo se registra una sola vez (después de completar).
    IF OLD.cash_handed_over_at IS NOT NULL
       AND NEW.cash_handed_over_at IS DISTINCT FROM OLD.cash_handed_over_at THEN
        RAISE EXCEPTION 'La entrega del efectivo de la operación % ya quedó registrada',
            OLD.display_number;
    END IF;

    RETURN NEW;
END;
$function$
;

-- DROP FUNCTION public.receipt_is_intact(text);

CREATE OR REPLACE FUNCTION public.receipt_is_intact(p_code text)
 RETURNS boolean
 LANGUAGE sql
 STABLE
AS $function$
    SELECT encode(sha256(convert_to(data::text, 'UTF8')), 'hex') = content_hash
    FROM receipts WHERE code = upper(p_code);
$function$
;

-- DROP FUNCTION public.set_updated_at();

CREATE OR REPLACE FUNCTION public.set_updated_at()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
BEGIN
    NEW.updated_at = now();
    RETURN NEW;
END;
$function$
;

-- DROP FUNCTION public.validate_ledger_entry_payment();

CREATE OR REPLACE FUNCTION public.validate_ledger_entry_payment()
 RETURNS trigger
 LANGUAGE plpgsql
AS $function$
DECLARE
    p payments%ROWTYPE;
    o operations%ROWTYPE;
    l settlements%ROWTYPE;
    expected_float_change bigint;
BEGIN
    IF NEW.type = 'cash_adjustment' THEN
        RETURN NEW;
    END IF;

    -- El pago debe estar completado y pertenecer a lo que se está registrando.
    SELECT * INTO p FROM payments WHERE id = NEW.payment_id;
    IF p.status <> 'completed' THEN
        RAISE EXCEPTION 'El pago % no está completado (estado: %)', p.id, p.status;
    END IF;
    IF p.operation_id IS DISTINCT FROM NEW.operation_id
       OR p.settlement_id IS DISTINCT FROM NEW.settlement_id THEN
        RAISE EXCEPTION 'El pago % no pertenece a la operación o liquidación del movimiento', p.id;
    END IF;

    IF NEW.operation_id IS NOT NULL THEN
        -- La operación debe ser del mismo corresponsal, estar completada y
        -- ser del mismo tipo; el cupo y la comisión deben moverse lo que ella dice.
        SELECT * INTO o FROM operations WHERE id = NEW.operation_id;
        IF o.agent_id <> NEW.agent_id THEN
            RAISE EXCEPTION 'La operación % es de otro corresponsal', o.display_number;
        END IF;
        IF o.status <> 'completed' THEN
            RAISE EXCEPTION 'La operación % no está completada (estado: %)', o.display_number, o.status;
        END IF;
        IF o.type::text <> NEW.type::text THEN
            RAISE EXCEPTION 'La operación % es de tipo % y el movimiento de tipo %', o.display_number, o.type, NEW.type;
        END IF;
        expected_float_change := CASE o.type
            WHEN 'deposit' THEN -o.charged_amount   -- el cupo baja lo que recibió en efectivo
            ELSE o.net_amount                       -- retiro y carga: el cupo sube
        END;
        IF NEW.float_change <> expected_float_change THEN
            RAISE EXCEPTION 'El cupo debe variar % según la operación % y varía %',
                expected_float_change, o.display_number, NEW.float_change;
        END IF;
        IF NEW.commission_change <> o.agent_commission THEN
            RAISE EXCEPTION 'La comisión debe variar % según la operación % y varía %',
                o.agent_commission, o.display_number, NEW.commission_change;
        END IF;
    ELSE
        -- La liquidación debe ser del mismo corresponsal, estar pagada y
        -- descontar exactamente su monto de la comisión acumulada.
        SELECT * INTO l FROM settlements WHERE id = NEW.settlement_id;
        IF l.agent_id <> NEW.agent_id THEN
            RAISE EXCEPTION 'La liquidación % es de otro corresponsal', l.display_number;
        END IF;
        IF l.status <> 'paid' THEN
            RAISE EXCEPTION 'La liquidación % no está pagada (estado: %)', l.display_number, l.status;
        END IF;
        IF NEW.commission_change <> -l.amount THEN
            RAISE EXCEPTION 'La comisión debe variar % según la liquidación % y varía %',
                -l.amount, l.display_number, NEW.commission_change;
        END IF;
    END IF;
    RETURN NEW;
END;
$function$
;

-- =====================================================================
-- TRIGGERS
-- =====================================================================

create trigger tg_admins_updated before
update
    on
    public.admins for each row execute function set_updated_at();

create trigger tg_agents_updated before
update
    on
    public.agents for each row execute function set_updated_at();

create trigger tg_agents_initial_balance after
insert
    on
    public.agents for each row execute function create_initial_balance();

create trigger tg_wallet_keys_immutable before
delete
    or
update
    on
    public.wallet_keys for each row execute function block_modification();

create trigger tg_agent_balances_updated before
update
    on
    public.agent_balances for each row execute function set_updated_at();

create trigger tg_operations_updated before
update
    on
    public.operations for each row execute function set_updated_at();

create trigger tg_operations_protect before
delete
    or
update
    on
    public.operations for each row execute function protect_operation();

create trigger tg_operations_issue_receipt after
update
    of status on
    public.operations for each row execute function issue_receipt_on_completion();

create trigger tg_receipts_immutable before
delete
    or
update
    on
    public.receipts for each row execute function block_modification();

create trigger tg_settlements_updated before
update
    on
    public.settlements for each row execute function set_updated_at();

create trigger tg_operation_events_immutable before
delete
    or
update
    on
    public.operation_events for each row execute function block_modification();

create trigger tg_payments_updated before
update
    on
    public.payments for each row execute function set_updated_at();

create trigger tg_issuances_copy before
insert
    on
    public.receipt_issuances for each row execute function mark_receipt_copy();

create trigger tg_issuances_immutable before
delete
    or
update
    on
    public.receipt_issuances for each row execute function block_modification();

create trigger tg_ledger_validate_payment before
insert
    on
    public.ledger_entries for each row execute function validate_ledger_entry_payment();

create trigger tg_ledger_immutable before
delete
    or
update
    on
    public.ledger_entries for each row execute function block_modification();

create trigger tg_payment_events_immutable before
delete
    or
update
    on
    public.payment_events for each row execute function block_modification();

-- =====================================================================
-- VISTAS
-- =====================================================================

-- public.v_admin_agent_summary source

CREATE OR REPLACE VIEW public.v_admin_agent_summary
AS SELECT c.id AS agent_id,
    c.name,
    c.contact_person,
    c.city,
    c.payment_pointer,
    s.float_balance,
    s.expected_cash,
    s.accrued_commission AS commission_payable,
    s.settled_commission_total AS commission_paid_total,
    o.operations_count,
    o.deposits_count,
    o.deposits_value,
    o.withdrawals_count,
    o.withdrawals_value,
    o.deposits_value + o.withdrawals_value AS total_moved,
    COALESCE(l.paid_today, 0::numeric) AS commission_paid_today
   FROM agents c
     JOIN agent_balances s ON s.agent_id = c.id
     CROSS JOIN LATERAL ( SELECT count(*) AS operations_count,
            count(*) FILTER (WHERE operations.type = 'deposit'::operation_type) AS deposits_count,
            COALESCE(sum(operations.requested_amount) FILTER (WHERE operations.type = 'deposit'::operation_type), 0::numeric) AS deposits_value,
            count(*) FILTER (WHERE operations.type = 'withdrawal'::operation_type) AS withdrawals_count,
            COALESCE(sum(operations.requested_amount) FILTER (WHERE operations.type = 'withdrawal'::operation_type), 0::numeric) AS withdrawals_value
           FROM operations
          WHERE operations.agent_id = c.id AND operations.status = 'completed'::operation_status AND (operations.type = ANY (ARRAY['deposit'::operation_type, 'withdrawal'::operation_type]))) o
     CROSS JOIN LATERAL ( SELECT sum(settlements.amount) AS paid_today
           FROM settlements
          WHERE settlements.agent_id = c.id AND settlements.status = 'paid'::settlement_status AND (settlements.paid_at AT TIME ZONE 'America/Bogota'::text)::date = (now() AT TIME ZONE 'America/Bogota'::text)::date) l;

COMMENT ON VIEW public.v_admin_agent_summary IS 'Fila por corresponsal para el panel admin: cupo, efectivo, comisión por pagar y totales movidos (valor M).';


-- public.v_agent_balance_check source

CREATE OR REPLACE VIEW public.v_agent_balance_check
AS SELECT c.id AS agent_id,
    c.name,
    s.float_balance,
    s.accrued_commission,
    s.settled_commission_total,
    s.expected_cash,
    o.cash_received_deposits,
    o.cash_paid_withdrawals,
    a.cash_adjustments,
    o.float_loaded,
    o.commission_earned,
    o.cash_received_deposits - o.cash_paid_withdrawals + a.cash_adjustments AS cash_calculated,
    o.float_loaded + o.cash_paid_withdrawals - o.cash_received_deposits AS float_calculated,
    s.expected_cash::numeric - (o.cash_received_deposits - o.cash_paid_withdrawals + a.cash_adjustments) AS cash_mismatch,
    s.float_balance::numeric - (o.float_loaded + o.cash_paid_withdrawals - o.cash_received_deposits) AS float_mismatch,
    (s.accrued_commission + s.settled_commission_total)::numeric - o.commission_earned AS commission_mismatch
   FROM agents c
     JOIN agent_balances s ON s.agent_id = c.id
     CROSS JOIN LATERAL ( SELECT COALESCE(sum(operations.charged_amount) FILTER (WHERE operations.type = 'deposit'::operation_type), 0::numeric) AS cash_received_deposits,
            COALESCE(sum(operations.net_amount) FILTER (WHERE operations.type = 'withdrawal'::operation_type), 0::numeric) AS cash_paid_withdrawals,
            COALESCE(sum(operations.net_amount) FILTER (WHERE operations.type = 'float_load'::operation_type), 0::numeric) AS float_loaded,
            COALESCE(sum(operations.agent_commission), 0::numeric) AS commission_earned
           FROM operations
          WHERE operations.agent_id = c.id AND operations.status = 'completed'::operation_status) o
     CROSS JOIN LATERAL ( SELECT COALESCE(sum(ledger_entries.cash_change), 0::numeric) AS cash_adjustments
           FROM ledger_entries
          WHERE ledger_entries.agent_id = c.id AND ledger_entries.type = 'cash_adjustment'::ledger_entry_type) a;

COMMENT ON VIEW public.v_agent_balance_check IS 'Totales del corresponsal (cupo, comisión, efectivo) y su recálculo desde las operaciones. Los descuadres deben ser 0.';


-- public.v_agent_history source

CREATE OR REPLACE VIEW public.v_agent_history
AS SELECT o.agent_id,
    COALESCE(o.completed_at, o.updated_at) AS occurred_at,
    o.type::text AS type,
    o.display_number AS reference,
    o.id AS operation_id,
    NULL::uuid AS settlement_id,
    m.id AS ledger_entry_id,
    o.status,
    o.requested_amount AS amount,
    COALESCE(m.float_before, u.float_after, 0::bigint) AS float_before,
    COALESCE(m.float_after, u.float_after, 0::bigint) AS float_after,
    COALESCE(m.commission_before, u.commission_after, 0::bigint) AS commission_before,
    COALESCE(m.commission_after, u.commission_after, 0::bigint) AS commission_after,
    COALESCE(m.cash_before, u.cash_after, 0::bigint) AS cash_before,
    COALESCE(m.cash_after, u.cash_after, 0::bigint) AS cash_after,
        CASE
            WHEN o.status = 'completed'::operation_status THEN NULL::text
            ELSE COALESCE(ev.details, pg.error_message)
        END AS reason
   FROM operations o
     LEFT JOIN ledger_entries m ON m.operation_id = o.id
     LEFT JOIN LATERAL ( SELECT x.float_after,
            x.commission_after,
            x.cash_after
           FROM ledger_entries x
          WHERE x.agent_id = o.agent_id AND x.created_at <= o.updated_at
          ORDER BY x.id DESC
         LIMIT 1) u ON m.id IS NULL
     LEFT JOIN LATERAL ( SELECT e.details
           FROM operation_events e
          WHERE e.operation_id = o.id AND e.new_status = o.status
          ORDER BY e.id DESC
         LIMIT 1) ev ON true
     LEFT JOIN LATERAL ( SELECT q.error_message
           FROM payments q
          WHERE q.operation_id = o.id AND q.error_message IS NOT NULL
          ORDER BY q.created_at DESC
         LIMIT 1) pg ON true
UNION ALL
 SELECT m.agent_id,
    m.created_at AS occurred_at,
    'cash_adjustment'::text AS type,
    NULL::text AS reference,
    NULL::uuid AS operation_id,
    NULL::uuid AS settlement_id,
    m.id AS ledger_entry_id,
    'completed'::operation_status AS status,
    m.cash_change AS amount,
    m.float_before,
    m.float_after,
    m.commission_before,
    m.commission_after,
    m.cash_before,
    m.cash_after,
    m.reason
   FROM ledger_entries m
  WHERE m.type = 'cash_adjustment'::ledger_entry_type
UNION ALL
 SELECT l.agent_id,
    COALESCE(l.paid_at, l.updated_at) AS occurred_at,
    'settlement'::text AS type,
    l.display_number AS reference,
    NULL::uuid AS operation_id,
    l.id AS settlement_id,
    m.id AS ledger_entry_id,
        CASE l.status
            WHEN 'paid'::settlement_status THEN 'completed'::text
            WHEN 'voided'::settlement_status THEN 'cancelled'::text
            ELSE 'pending'::text
        END::operation_status AS status,
    l.amount,
    COALESCE(m.float_before, u.float_after, 0::bigint) AS float_before,
    COALESCE(m.float_after, u.float_after, 0::bigint) AS float_after,
    COALESCE(m.commission_before, u.commission_after, 0::bigint) AS commission_before,
    COALESCE(m.commission_after, u.commission_after, 0::bigint) AS commission_after,
    COALESCE(m.cash_before, u.cash_after, 0::bigint) AS cash_before,
    COALESCE(m.cash_after, u.cash_after, 0::bigint) AS cash_after,
        CASE
            WHEN l.status = 'paid'::settlement_status THEN NULL::text
            ELSE COALESCE(l.void_reason, pg.error_message)
        END AS reason
   FROM settlements l
     LEFT JOIN ledger_entries m ON m.settlement_id = l.id
     LEFT JOIN LATERAL ( SELECT x.float_after,
            x.commission_after,
            x.cash_after
           FROM ledger_entries x
          WHERE x.agent_id = l.agent_id AND x.created_at <= l.updated_at
          ORDER BY x.id DESC
         LIMIT 1) u ON m.id IS NULL
     LEFT JOIN LATERAL ( SELECT q.error_message
           FROM payments q
          WHERE q.settlement_id = l.id AND q.error_message IS NOT NULL
          ORDER BY q.created_at DESC
         LIMIT 1) pg ON true;

COMMENT ON VIEW public.v_agent_history IS 'Historial del corresponsal: operaciones en todos los estados, ajustes de efectivo y pagos de comisión, con antes y después.';


-- public.v_wallet_traceability source

CREATE OR REPLACE VIEW public.v_wallet_traceability
AS SELECT w.payment_pointer,
    w.side,
    p.id AS payment_id,
    p.rail,
    p.direction,
    p.status,
    p.amount,
    p.currency,
    p.source_payment_pointer,
    p.destination_payment_pointer,
    o.id AS operation_id,
    o.display_number AS operation_number,
    o.type AS operation_type,
    l.id AS settlement_id,
    l.display_number AS settlement_number,
    p.op_incoming_payment_url,
    p.op_outgoing_payment_url,
    p.created_at,
    p.completed_at
   FROM payments p
     CROSS JOIN LATERAL ( VALUES (p.source_payment_pointer,'source'::text), (p.destination_payment_pointer,'destination'::text)) w(payment_pointer, side)
     LEFT JOIN operations o ON o.id = p.operation_id
     LEFT JOIN settlements l ON l.id = p.settlement_id;

COMMENT ON VIEW public.v_wallet_traceability IS 'Recorrido de cada payment pointer: pagos donde participó como origen o destino.';
