-- Dev seed: 1 facility, 1 coordinator, 3 residents, 2 vendors
INSERT INTO facilities (id, name, address, created_at) VALUES
  ('00000000-0000-0000-0000-000000000001', 'Sunrise Senior Living', '100 Maple Ave, Detroit MI 48201', NOW());

-- Password: DevPassword123! (hashed by ASP.NET Core Identity at runtime — placeholder)
INSERT INTO asp_net_users (id, facility_id, user_name, normalized_user_name, email, normalized_email, email_confirmed, password_hash, security_stamp, concurrency_stamp, fcm_token, created_at) VALUES
  ('00000000-0000-0000-0000-000000000010', '00000000-0000-0000-0000-000000000001', 'coordinator@sunrise.com', 'COORDINATOR@SUNRISE.COM', 'coordinator@sunrise.com', 'COORDINATOR@SUNRISE.COM', true, 'PLACEHOLDER_HASH', '', gen_random_uuid()::text, NULL, NOW());

INSERT INTO residents (id, facility_id, first_name, last_name, needs_wheelchair, needs_oxygen, needs_walker, driver_notes, is_active) VALUES
  (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'Margaret', 'Wilson', true, false, false, 'Power wheelchair — needs ramp van. Slow transfer, allow 10 min.', true),
  (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'Robert', 'Chen', false, true, false, 'Portable oxygen concentrator in carry bag. Must stay upright.', true),
  (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'Dorothy', 'Martinez', false, false, true, 'Uses walker. Needs extra time to board.', true);

INSERT INTO vendors (id, facility_id, name, phone_number, type, capability_tier, is_active) VALUES
  (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'Valley Medical Transport', '+15555550101', 'wheelchair', 'smart', true),
  (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'City Rides NEMT', '+15555550102', 'ambulatory', 'basic', true);
