-- Row Level Security for rides table
ALTER TABLE rides ENABLE ROW LEVEL SECURITY;

CREATE POLICY facility_isolation ON rides
  USING (facility_id = current_setting('app.current_facility_id')::uuid);
