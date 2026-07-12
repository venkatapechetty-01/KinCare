-- Row Level Security for vendors table
ALTER TABLE vendors ENABLE ROW LEVEL SECURITY;

CREATE POLICY facility_isolation ON vendors
  USING (facility_id = current_setting('app.current_facility_id')::uuid);
