alter table bots.bots
    add column if not exists last_error_code text,
    add column if not exists last_error_message text;

alter table bots.bots
    drop constraint if exists bots_last_error_code_length_check,
    add constraint bots_last_error_code_length_check
        check (last_error_code is null or length(last_error_code) between 1 and 100),
    drop constraint if exists bots_last_error_message_length_check,
    add constraint bots_last_error_message_length_check
        check (last_error_message is null or length(last_error_message) between 1 and 500);
