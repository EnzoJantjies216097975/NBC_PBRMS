-- ===========================================================================
-- NBC PBRMS — 0007 realtime
-- Broadcast notification inserts so the in-app bell/inbox update live without a
-- page refresh. RLS still applies to what each client may read.
-- ===========================================================================

alter publication supabase_realtime add table notifications;
