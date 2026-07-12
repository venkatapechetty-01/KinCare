-- Row Level Security for ride_events table (append-only, no delete/update)
ALTER TABLE ride_events ENABLE ROW LEVEL SECURITY;

CREATE POLICY facility_isolation ON ride_events
  USING (
    ride_id IN (
      SELECT id FROM rides
      WHERE facility_id = current_setting('app.current_facility_id')::uuid
    )
  );
