-- deskbook schema. Every statement is safe to run again: "deskbook db init" applies this whole file.

-- Trades of the fictional desk, as entered. A trade is never updated in place.
CREATE TABLE IF NOT EXISTS trade (
    id              text        PRIMARY KEY,
    trade_date      date        NOT NULL,
    counterparty    text        NOT NULL,
    bought_currency char(3)     NOT NULL,
    bought_amount   numeric     NOT NULL CHECK (bought_amount > 0),
    sold_currency   char(3)     NOT NULL,
    sold_amount     numeric     NOT NULL CHECK (sold_amount > 0),
    entered_at      timestamptz NOT NULL DEFAULT now(),
    CHECK (bought_currency <> sold_currency)
);

-- One row per request to the Bank of Canada. covered_to is the last day the answer vouches for: the latest
-- day that had rates. Days after it (even if requested) are unknown, not holidays. NULL when no day had rates.
CREATE TABLE IF NOT EXISTS rate_fetch (
    id             bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    series_group   text        NOT NULL,
    requested_from date        NOT NULL,
    requested_to   date        NOT NULL CHECK (requested_to >= requested_from),
    covered_to     date        CHECK (covered_to BETWEEN requested_from AND requested_to),
    fetched_at     timestamptz NOT NULL,
    source_url     text        NOT NULL
);

-- The current value of each published rate: Canadian dollars per one unit of the currency.
CREATE TABLE IF NOT EXISTS rate (
    rate_date    date    NOT NULL,
    currency     char(3) NOT NULL,
    cad_per_unit numeric NOT NULL CHECK (cad_per_unit > 0),
    fetch_id     bigint  NOT NULL REFERENCES rate_fetch (id),
    PRIMARY KEY (rate_date, currency)
);

-- When a later fetch returns a different value for a stored day, the old value is kept here.
CREATE TABLE IF NOT EXISTS rate_revision (
    id               bigint  GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    rate_date        date    NOT NULL,
    currency         char(3) NOT NULL,
    old_cad_per_unit numeric NOT NULL,
    new_cad_per_unit numeric NOT NULL,
    fetch_id         bigint  NOT NULL REFERENCES rate_fetch (id)
);

-- Reconciliation audit trail. Each run keeps the SHA-256 of the confirmation file it read, so a run can be traced to
-- the exact bytes. Runs, breaks and resolutions are only ever inserted: the trigger below refuses UPDATE and DELETE.
CREATE TABLE IF NOT EXISTS recon_run (
    id                     bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_at                 timestamptz NOT NULL,
    source_name            text        NOT NULL,
    source_sha256          text        NOT NULL CHECK (source_sha256 ~ '^[0-9a-f]{64}$'),
    trades_compared        int         NOT NULL,
    confirmations_compared int         NOT NULL,
    matched                int         NOT NULL,
    break_count            int         NOT NULL
);

CREATE TABLE IF NOT EXISTS recon_break (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    run_id             bigint NOT NULL REFERENCES recon_run (id),
    kind               text   NOT NULL CHECK (kind IN ('MissingConfirmation', 'UnknownTrade', 'DuplicateConfirmation', 'FieldMismatch')),
    trade_id           text,
    confirmation_id    text,
    field              text,
    desk_value         text,
    counterparty_value text,
    detail             text   NOT NULL
);

-- A resolution is a note on a break: who looked at it, when, and what they found. A break can collect several.
CREATE TABLE IF NOT EXISTS recon_resolution (
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    break_id    bigint      NOT NULL REFERENCES recon_break (id),
    resolved_at timestamptz NOT NULL,
    resolved_by text        NOT NULL CHECK (length(trim(resolved_by)) > 0),
    note        text        NOT NULL CHECK (length(trim(note)) > 0)
);

CREATE OR REPLACE FUNCTION deskbook_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% rows are never changed or deleted (append-only)', TG_TABLE_NAME;
END;
$$;

CREATE OR REPLACE TRIGGER trade_append_only BEFORE UPDATE OR DELETE ON trade
    FOR EACH ROW EXECUTE FUNCTION deskbook_append_only();
CREATE OR REPLACE TRIGGER recon_run_append_only BEFORE UPDATE OR DELETE ON recon_run
    FOR EACH ROW EXECUTE FUNCTION deskbook_append_only();
CREATE OR REPLACE TRIGGER recon_break_append_only BEFORE UPDATE OR DELETE ON recon_break
    FOR EACH ROW EXECUTE FUNCTION deskbook_append_only();
CREATE OR REPLACE TRIGGER recon_resolution_append_only BEFORE UPDATE OR DELETE ON recon_resolution
    FOR EACH ROW EXECUTE FUNCTION deskbook_append_only();
