import { z } from 'zod';

// ── Enums ────────────────────────────────────────────────────────────────────

export const RideStatusSchema = z.enum([
  'Dispatched', 'Confirmed', 'EnRoute', 'Arrived', 'PickedUp', 'AtDestination', 'Dropped', 'Completed', 'Cancelled',
]);

export const DispatchChannelSchema = z.enum([
  'SmsNemt', 'SmsTaxi', 'Broker',
]);

export const VendorTypeSchema = z.enum(['Wheelchair', 'Ambulatory']);

export const DispatchMethodSchema = z.enum(['SmsNemt', 'SmsTaxi', 'Broker']);

export const CapabilityTierSchema = z.enum(['Basic', 'Smart']);

export const PlanTierSchema = z.enum(['Starter', 'Professional', 'Enterprise']);

export const UserRoleSchema = z.enum(['SuperAdmin', 'OrgAdmin', 'FacilityAdmin']);

// ── Domain schemas ────────────────────────────────────────────────────────────

export const ResidentSchema = z.object({
  id: z.string().uuid(),
  facilityId: z.string().uuid(),
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  needsWheelchair: z.boolean(),
  needsOxygen: z.boolean(),
  needsStretcher: z.boolean(),
  needsWalker: z.boolean(),
  driverNotes: z.string().optional(),
  isActive: z.boolean().optional(),
});

export const VendorSchema = z.object({
  id: z.string().uuid(),
  facilityId: z.string().uuid(),
  name: z.string().min(1),
  phoneNumber: z.string().min(1),
  vendorType: VendorTypeSchema,
  dispatchMethod: DispatchMethodSchema,
  capabilityTier: CapabilityTierSchema,
  photoUrl: z.string().url().nullable().optional(),
  isActive: z.boolean(),
});

export const RideEventSchema = z.object({
  fromStatus: z.string(),
  toStatus: z.string(),
  triggeredBy: z.string(),
  notes: z.string().nullable().optional(),
  occurredAt: z.string().datetime({ offset: true }),
});

export const RideSummarySchema = z.object({
  id: z.string().uuid(),
  facilityId: z.string().uuid(),
  organizationId: z.string().uuid(),
  residentId: z.string().uuid().optional(),
  vendorId: z.string().uuid().nullable().optional(),
  status: RideStatusSchema,
  dispatchChannel: DispatchChannelSchema,
  pickupTime: z.string().datetime({ offset: true }),
  pickupAddress: z.string(),
  destinationAddress: z.string(),
  trackingToken: z.string().nullable().optional(),
  lastKnownLat: z.number().nullable().optional(),
  lastKnownLng: z.number().nullable().optional(),
  lastLocationAt: z.string().datetime({ offset: true }).nullable().optional(),
  createdAt: z.string().datetime({ offset: true }),
  residentName: z.string().optional(),
  vendorName: z.string().nullable().optional(),
});

export const RideDetailSchema = RideSummarySchema.extend({
  residentName: z.string(),
  vendorName: z.string().nullable().optional(),
  vendorPhone: z.string().nullable().optional(),
  externalTripId: z.string().nullable().optional(),
  events: z.array(RideEventSchema),
});

export const ActiveRideLocationSchema = z.object({
  id: z.string().uuid(),
  facilityName: z.string(),
  residentName: z.string(),
  vendorName: z.string().nullable().optional(),
  vendorPhone: z.string().nullable().optional(),
  vendorPhotoUrl: z.string().nullable().optional(),
  status: RideStatusSchema,
  dispatchChannel: DispatchChannelSchema,
  pickupAddress: z.string(),
  destinationAddress: z.string(),
  pickupTime: z.string().datetime({ offset: true }),
  lat: z.number(),
  lng: z.number(),
  lastLocationAt: z.string().datetime({ offset: true }).nullable().optional(),
});

export const DispatchOfferSchema = z.object({
  id: z.string().uuid(),
  vendorId: z.string().uuid(),
  vendorName: z.string(),
  status: z.string(),
  sentAt: z.string().datetime({ offset: true }),
  respondedAt: z.string().datetime({ offset: true }).nullable().optional(),
});

export const OrganizationSchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  planTier: PlanTierSchema,
  isActive: z.boolean(),
  billingEmail: z.string().email(),
  createdAt: z.string().datetime({ offset: true }),
});

export const FacilitySchema = z.object({
  id: z.string().uuid(),
  name: z.string(),
  address: z.string(),
  timezone: z.string(),
  activeRides: z.number().int().nonnegative(),
});

export const OrgUserSchema = z.object({
  id: z.string().uuid(),
  firstName: z.string(),
  lastName: z.string(),
  email: z.string().email(),
  role: z.string(),
  facilityId: z.string().uuid().nullable().optional(),
  facilityName: z.string().nullable().optional(),
  isActive: z.boolean(),
});

export const FacilityMetricsSchema = z.object({
  facilityId: z.string().uuid(),
  facilityName: z.string(),
  totalRides: z.number().int(),
  completedRides: z.number().int(),
  cancelledRides: z.number().int(),
});

export const LoginResponseSchema = z.object({
  accessToken: z.string(),
  refreshToken: z.string(),
  role: z.string(),
  organizationId: z.string().uuid(),
  facilityId: z.string().uuid().optional(),
});

export const RegisterResponseSchema = z.object({
  accessToken: z.string(),
  refreshToken: z.string(),
  organizationId: z.string().uuid(),
  facilityId: z.string().uuid().optional(),
  userId: z.string().uuid(),
});

export const InviteDetailsSchema = z.object({
  email: z.string().email(),
  role: z.string(),
  organizationName: z.string(),
  facilityName: z.string().optional(),
});

export const BookRideResponseSchema = z.object({
  id: z.string().uuid(),
  status: RideStatusSchema,
  dispatchChannel: DispatchChannelSchema,
  vendorId: z.string().uuid().nullable().optional(),
});

export const TodayCountSchema = z.object({ count: z.number().int().nonnegative() });

// ── List wrappers ─────────────────────────────────────────────────────────────

export const ResidentListSchema = z.array(ResidentSchema);
export const VendorListSchema = z.array(VendorSchema);
export const RideSummaryListSchema = z.array(RideSummarySchema);
export const ActiveRideLocationListSchema = z.array(ActiveRideLocationSchema);
export const FacilityListSchema = z.array(FacilitySchema);
export const OrgUserListSchema = z.array(OrgUserSchema);
export const FacilityMetricsListSchema = z.array(FacilityMetricsSchema);
export const DispatchOfferListSchema = z.array(DispatchOfferSchema);

export const RideHistorySchema = z.object({
  items: RideSummaryListSchema,
  totalCount: z.number().int().nonnegative(),
});

// ── Inferred TypeScript types (replace hand-written interfaces) ───────────────

export type Resident        = z.infer<typeof ResidentSchema>;
export type Vendor          = z.infer<typeof VendorSchema>;
export type RideEvent       = z.infer<typeof RideEventSchema>;
export type RideSummary     = z.infer<typeof RideSummarySchema>;
export type RideDetail      = z.infer<typeof RideDetailSchema>;
export type ActiveRideLocation = z.infer<typeof ActiveRideLocationSchema>;
export type DispatchOffer   = z.infer<typeof DispatchOfferSchema>;
export type Organization    = z.infer<typeof OrganizationSchema>;
export type Facility        = z.infer<typeof FacilitySchema>;
export type OrgUser         = z.infer<typeof OrgUserSchema>;
export type FacilityMetrics = z.infer<typeof FacilityMetricsSchema>;
export type LoginResponse   = z.infer<typeof LoginResponseSchema>;
export type RegisterResponse = z.infer<typeof RegisterResponseSchema>;
export type InviteDetails   = z.infer<typeof InviteDetailsSchema>;
export type BookRideResponse = z.infer<typeof BookRideResponseSchema>;
export type RideStatus      = z.infer<typeof RideStatusSchema>;
export type DispatchChannel = z.infer<typeof DispatchChannelSchema>;
export type PlanTier        = z.infer<typeof PlanTierSchema>;
export type UserRole        = z.infer<typeof UserRoleSchema>;
