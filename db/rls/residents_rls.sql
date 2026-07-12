-- Row Level Security for residents table
ALTER TABLE residents ENABLE ROW LEVEL SECURITY;

CREATE POLICY facility_isolation ON residents
  USING (facility_id = current_setting('app.current_facility_id')::uuid);
