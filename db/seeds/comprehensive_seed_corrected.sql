-- =====================================================
-- COMPREHENSIVE SEED DATA FOR KINCARE (EF Core PascalCase Schema)
-- Includes full multi-tenant hierarchy with example data
-- =====================================================

-- =====================================================
-- ORGANIZATION 1: Sunrise Senior Living Group
-- Plan: Professional (has Uber Health access)
-- =====================================================
INSERT INTO organizations ("Id", "Name", "PlanTier", "IsActive", "BillingEmail", "BrokerEnabled", "CreatedAt")
VALUES
  ('11111111-1111-1111-1111-111111111111',
   'Sunrise Senior Living Group',
   'Professional',
   true,
   'billing@sunrisesenior.com',
   false,
   NOW() - INTERVAL '6 months')
ON CONFLICT ("Id") DO NOTHING;

-- Facilities for Sunrise
INSERT INTO facilities ("Id", "OrganizationId", "Name", "Address", "Timezone", "UberHealthEnabled", "IsActive", "CreatedAt")
VALUES
  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'Sunrise Maple Grove',
   '1250 Maple Avenue, Detroit, MI 48201',
   'America/Detroit',
   true,
   true,
   NOW() - INTERVAL '6 months'),

  ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab',
   '11111111-1111-1111-1111-111111111111',
   'Sunrise Oak Park',
   '890 Oak Street, Detroit, MI 48237',
   'America/Detroit',
   true,
   true,
   NOW() - INTERVAL '5 months')
ON CONFLICT ("Id") DO NOTHING;

-- =====================================================
-- RESIDENTS - Sunrise Maple Grove (Diverse needs)
-- =====================================================
INSERT INTO residents ("Id", "FacilityId", "FirstName", "LastName", "NeedsWheelchair", "NeedsOxygen", "NeedsStretcher", "NeedsWalker", "DriverNotes", "IsActive", "CreatedAt")
VALUES
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Margaret',
   'Wilson',
   true,
   false,
   false,
   false,
   'Power wheelchair - needs ramp van. Prefers window seat. Please allow 10 minutes for safe transfer.',
   true,
   NOW() - INTERVAL '4 months'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb02',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Robert',
   'Chen',
   false,
   true,
   false,
   false,
   'Portable oxygen concentrator in carry bag. Must stay upright at all times. Very friendly and talkative.',
   true,
   NOW() - INTERVAL '3 months'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb03',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Dorothy',
   'Martinez',
   false,
   false,
   false,
   true,
   'Uses walker. Needs extra time to board. Prefers to sit in front passenger seat if possible.',
   true,
   NOW() - INTERVAL '5 months'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb04',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'James',
   'Thompson',
   true,
   true,
   false,
   false,
   'Wheelchair + oxygen. Requires large wheelchair van with oxygen holder. Very important to be gentle.',
   true,
   NOW() - INTERVAL '2 months'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb05',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Eleanor',
   'Rodriguez',
   false,
   false,
   false,
   false,
   'Ambulatory, no special equipment needed. Enjoys conversation during rides.',
   true,
   NOW() - INTERVAL '1 month'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb06',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'William',
   'Anderson',
   false,
   false,
   true,
   false,
   'Stretcher required. Cannot sit up. Medical transport van only. Handle with extreme care.',
   true,
   NOW() - INTERVAL '1 month'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb07',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Linda',
   'Davis',
   false,
   false,
   false,
   true,
   'Walker and cane. Needs assistance getting in/out of vehicle. Very kind and patient.',
   true,
   NOW() - INTERVAL '4 weeks'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb08',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Henry',
   'Garcia',
   true,
   false,
   false,
   false,
   'Manual wheelchair. Can transfer with minimal assistance. Prefers to keep wheelchair in vehicle.',
   true,
   NOW() - INTERVAL '2 weeks')
ON CONFLICT ("Id") DO NOTHING;

-- Residents - Sunrise Oak Park
INSERT INTO residents ("Id", "FacilityId", "FirstName", "LastName", "NeedsWheelchair", "NeedsOxygen", "NeedsStretcher", "NeedsWalker", "DriverNotes", "IsActive", "CreatedAt")
VALUES
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb09',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab',
   'Patricia',
   'Miller',
   false,
   false,
   false,
   true,
   'Walker. Slow movements. Please be patient and allow plenty of time.',
   true,
   NOW() - INTERVAL '3 months'),

  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb10',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab',
   'Charles',
   'Lee',
   false,
   true,
   false,
   false,
   'Oxygen tank on cart. Needs help loading oxygen into vehicle. Very independent otherwise.',
   true,
   NOW() - INTERVAL '2 months')
ON CONFLICT ("Id") DO NOTHING;

-- =====================================================
-- VENDORS - Sunrise Maple Grove
-- =====================================================
INSERT INTO vendors ("Id", "FacilityId", "Name", "PhoneNumber", "VendorType", "DispatchMethod", "CapabilityTier", "IsActive", "CreatedAt")
VALUES
  ('cccccccc-cccc-cccc-cccc-cccccccccc01',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Valley Medical Transport',
   '+13135551001',
   'Wheelchair',
   'SmsNemt',
   'Smart',
   true,
   NOW() - INTERVAL '6 months'),

  ('cccccccc-cccc-cccc-cccc-cccccccccc02',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Detroit NEMT Services',
   '+13135551002',
   'Wheelchair',
   'SmsNemt',
   'Basic',
   true,
   NOW() - INTERVAL '5 months'),

  ('cccccccc-cccc-cccc-cccc-cccccccccc03',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Quick Ride Taxi',
   '+13135551003',
   'Ambulatory',
   'SmsTaxi',
   'Basic',
   true,
   NOW() - INTERVAL '4 months'),

  ('cccccccc-cccc-cccc-cccc-cccccccccc04',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'Premier Wheelchair Transport',
   '+13135551004',
   'Wheelchair',
   'SmsNemt',
   'Smart',
   true,
   NOW() - INTERVAL '3 months'),

  ('cccccccc-cccc-cccc-cccc-cccccccccc05',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   'City Ambulatory Services',
   '+13135551005',
   'Ambulatory',
   'SmsTaxi',
   'Smart',
   true,
   NOW() - INTERVAL '2 months')
ON CONFLICT ("Id") DO NOTHING;

-- Vendors - Sunrise Oak Park
INSERT INTO vendors ("Id", "FacilityId", "Name", "PhoneNumber", "VendorType", "DispatchMethod", "CapabilityTier", "IsActive", "CreatedAt")
VALUES
  ('cccccccc-cccc-cccc-cccc-cccccccccc06',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab',
   'Oak Park Medical Transport',
   '+13135552001',
   'Wheelchair',
   'SmsNemt',
   'Basic',
   true,
   NOW() - INTERVAL '4 months')
ON CONFLICT ("Id") DO NOTHING;

-- =====================================================
-- RIDES - Today's rides with various statuses
-- =====================================================

-- Ride 1: Confirmed ride - Margaret to dialysis (WHEELCHAIR)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "TrackingToken", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd01',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb01',  -- Margaret Wilson
   'cccccccc-cccc-cccc-cccc-cccccccccc01',  -- Valley Medical Transport
   'Confirmed',
   'SmsNemt',
   NOW() + INTERVAL '2 hours',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Detroit Medical Center - Dialysis, 4201 St Antoine, Detroit, MI 48201',
   'track_' || md5(random()::text),
   NOW() - INTERVAL '3 hours')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 2: EnRoute - Robert to cardiology appointment (OXYGEN)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "TrackingToken", "LastKnownLat", "LastKnownLng", "LastLocationAt", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd02',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb02',  -- Robert Chen
   'cccccccc-cccc-cccc-cccc-cccccccccc02',  -- Detroit NEMT
   'EnRoute',
   'SmsNemt',
   NOW() + INTERVAL '45 minutes',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Henry Ford Health System - Cardiology, 2799 W Grand Blvd, Detroit, MI 48202',
   'track_' || md5(random()::text),
   42.3526,
   -83.0689,
   NOW() - INTERVAL '5 minutes',
   NOW() - INTERVAL '1 hour')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 3: Arrived - Dorothy at physical therapy (WALKER)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "TrackingToken", "LastKnownLat", "LastKnownLng", "LastLocationAt", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd03',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb03',  -- Dorothy Martinez
   'cccccccc-cccc-cccc-cccc-cccccccccc05',  -- City Ambulatory (Smart)
   'Arrived',
   'SmsTaxi',
   NOW() + INTERVAL '30 minutes',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Rehabilitation Institute, 261 Mack Ave, Detroit, MI 48201',
   'track_' || md5(random()::text),
   42.3314,
   -83.0458,
   NOW() - INTERVAL '2 minutes',
   NOW() - INTERVAL '45 minutes')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 4: Dispatched - James to hospital (WHEELCHAIR + OXYGEN)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd04',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb04',  -- James Thompson
   'cccccccc-cccc-cccc-cccc-cccccccccc04',  -- Premier Wheelchair
   'Dispatched',
   'SmsNemt',
   NOW() + INTERVAL '4 hours',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Beaumont Hospital, 3601 W 13 Mile Rd, Royal Oak, MI 48073',
   NOW() - INTERVAL '15 minutes')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 5: Confirmed - Eleanor to dentist (AMBULATORY - UberHealth could be used)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd05',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb05',  -- Eleanor Rodriguez
   'cccccccc-cccc-cccc-cccc-cccccccccc03',  -- Quick Ride Taxi
   'Confirmed',
   'SmsTaxi',
   NOW() + INTERVAL '3 hours',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Smile Dental Care, 19925 W 12 Mile Rd, Southfield, MI 48076',
   NOW() - INTERVAL '30 minutes')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 6: Dropped - William from hospital (STRETCHER)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd06',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb06',  -- William Anderson
   'cccccccc-cccc-cccc-cccc-cccccccccc01',  -- Valley Medical
   'Dropped',
   'SmsNemt',
   NOW() - INTERVAL '1 hour',
   'DMC Harper University Hospital, 3990 John R St, Detroit, MI 48201',
   '1250 Maple Avenue, Detroit, MI 48201',
   NOW() - INTERVAL '3 hours')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 7: EnRoute - Linda to eye doctor (WALKER)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "TrackingToken", "LastKnownLat", "LastKnownLng", "LastLocationAt", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd07',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb07',  -- Linda Davis
   'cccccccc-cccc-cccc-cccc-cccccccccc05',  -- City Ambulatory (Smart)
   'EnRoute',
   'SmsTaxi',
   NOW() + INTERVAL '1 hour',
   '1250 Maple Avenue, Detroit, MI 48201',
   'EyeCare Specialists, 29200 Vassar St, Livonia, MI 48152',
   'track_' || md5(random()::text),
   42.3601,
   -83.0582,
   NOW() - INTERVAL '3 minutes',
   NOW() - INTERVAL '25 minutes')
ON CONFLICT ("Id") DO NOTHING;

-- Ride 8: Confirmed - Henry to podiatrist (WHEELCHAIR)
INSERT INTO rides ("Id", "FacilityId", "OrganizationId", "ResidentId", "VendorId", "Status", "DispatchChannel", "PickupTime", "PickupAddress", "DestinationAddress", "TrackingToken", "CreatedAt")
VALUES
  ('dddddddd-dddd-dddd-dddd-dddddddddd08',
   'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
   '11111111-1111-1111-1111-111111111111',
   'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbb08',  -- Henry Garcia
   'cccccccc-cccc-cccc-cccc-cccccccccc04',  -- Premier Wheelchair (Smart)
   'Confirmed',
   'SmsNemt',
   NOW() + INTERVAL '5 hours',
   '1250 Maple Avenue, Detroit, MI 48201',
   'Detroit Foot Care, 18101 Oakwood Blvd, Dearborn, MI 48124',
   'track_' || md5(random()::text),
   NOW() - INTERVAL '20 minutes')
ON CONFLICT ("Id") DO NOTHING;

-- =====================================================
-- RIDE EVENTS - Status history for rides
-- =====================================================

-- Events for Ride 1 (Margaret - Confirmed)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd01', '', 'Dispatched', 'system', 'Ride dispatched via SMS to Valley Medical Transport', NOW() - INTERVAL '3 hours'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd01', 'Dispatched', 'Confirmed', 'vendor_sms', 'Vendor confirmed via SMS reply - Driver: Mike Johnson', NOW() - INTERVAL '2 hours 45 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 2 (Robert - EnRoute)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd02', '', 'Dispatched', 'system', 'Ride dispatched via SMS to Detroit NEMT', NOW() - INTERVAL '1 hour'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd02', 'Dispatched', 'Confirmed', 'vendor_sms', 'Vendor confirmed - Driver: Sarah Williams', NOW() - INTERVAL '50 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd02', 'Confirmed', 'EnRoute', 'vendor_sms', 'Driver on the way to pickup', NOW() - INTERVAL '15 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 3 (Dorothy - Arrived)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd03', '', 'Dispatched', 'system', 'Ride dispatched via SMS to City Ambulatory', NOW() - INTERVAL '45 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd03', 'Dispatched', 'Confirmed', 'vendor_sms', 'Confirmed by driver Tom Harris', NOW() - INTERVAL '40 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd03', 'Confirmed', 'EnRoute', 'tracking_page', 'Driver marked on the way via GPS tracker', NOW() - INTERVAL '20 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd03', 'EnRoute', 'Arrived', 'tracking_page', 'Arrived at destination - GPS confirmed', NOW() - INTERVAL '2 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 4 (James - Dispatched)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd04', '', 'Dispatched', 'system', 'Ride dispatched via SMS to Premier Wheelchair - Special needs: Wheelchair + Oxygen', NOW() - INTERVAL '15 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 5 (Eleanor - Confirmed)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd05', '', 'Dispatched', 'system', 'Ride dispatched via SMS to Quick Ride Taxi', NOW() - INTERVAL '30 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd05', 'Dispatched', 'Confirmed', 'vendor_sms', 'Driver David Kim confirmed pickup', NOW() - INTERVAL '25 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 6 (William - Dropped)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd06', '', 'Dispatched', 'system', 'Ride dispatched - Medical stretcher transport from hospital', NOW() - INTERVAL '3 hours'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd06', 'Dispatched', 'Confirmed', 'vendor_sms', 'Confirmed by Valley Medical - Stretcher van assigned', NOW() - INTERVAL '2 hours 50 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd06', 'Confirmed', 'EnRoute', 'vendor_sms', 'Driver en route to hospital pickup', NOW() - INTERVAL '1 hour 30 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd06', 'EnRoute', 'Arrived', 'vendor_sms', 'Arrived at hospital - loading patient', NOW() - INTERVAL '45 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd06', 'Arrived', 'Dropped', 'vendor_sms', 'Resident safely returned to facility', NOW() - INTERVAL '10 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 7 (Linda - EnRoute)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd07', '', 'Dispatched', 'system', 'Ride dispatched via SMS to City Ambulatory', NOW() - INTERVAL '25 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd07', 'Dispatched', 'Confirmed', 'vendor_sms', 'Driver Lisa Chen confirmed', NOW() - INTERVAL '22 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd07', 'Confirmed', 'EnRoute', 'tracking_page', 'On the way - GPS active', NOW() - INTERVAL '10 minutes')
ON CONFLICT DO NOTHING;

-- Events for Ride 8 (Henry - Confirmed)
INSERT INTO ride_events ("Id", "RideId", "FromStatus", "ToStatus", "TriggeredBy", "Notes", "OccurredAt")
VALUES
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd08', '', 'Dispatched', 'system', 'Ride dispatched to Premier Wheelchair - Manual wheelchair transport', NOW() - INTERVAL '20 minutes'),
  (gen_random_uuid(), 'dddddddd-dddd-dddd-dddd-dddddddddd08', 'Dispatched', 'Confirmed', 'vendor_sms', 'Confirmed - wheelchair-accessible van ready', NOW() - INTERVAL '18 minutes')
ON CONFLICT DO NOTHING;

-- =====================================================
-- SUMMARY
-- =====================================================
-- Organization: Sunrise Senior Living Group (Professional plan)
-- Facilities: 2 (Maple Grove, Oak Park)
-- Residents: 10 total (8 at Maple Grove, 2 at Oak Park)
--   - 4 wheelchair users
--   - 3 oxygen users
--   - 1 stretcher user
--   - 4 walker users
--   - 1 fully ambulatory
-- Vendors: 6 total (5 at Maple Grove, 1 at Oak Park)
--   - 4 wheelchair/NEMT vendors
--   - 2 ambulatory/taxi vendors
--   - 3 with Smart GPS tracking
-- Rides: 8 rides scheduled for today with various statuses
--   - 2 Dispatched
--   - 3 Confirmed
--   - 2 EnRoute (with GPS data)
--   - 1 Arrived
--   - 1 Dropped
-- =====================================================
