-- Row Level Security for facilities table
ALTER TABLE facilities ENABLE ROW LEVEL SECURITY;

CREATE POLICY facility_isolation ON facilities
  USING (id = current_setting('app.current_facility_id')::uuid);
