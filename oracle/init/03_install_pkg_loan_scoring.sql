-- =============================================================================
-- 03_install_pkg_loan_scoring.sql — compiles package PKG_LOAN_SCORING.
--
-- Why a separate file: the gvenzl/oracle-free image runs only *.sql, *.sql.gz, *.sql.zip
-- and *.sh from /container-entrypoint-initdb.d and ignores .pks/.pkb (names fixed by the
-- contract). In SQL*Plus @@ runs a file relative to the CURRENT script, so it does not
-- depend on the working directory.
-- Order matters: the body compiles against an already existing specification.
-- See docs/DECISIONS.md, ADR-004.
-- =============================================================================

@@03_pkg_loan_scoring.pks
@@04_pkg_loan_scoring.pkb
