import { z } from 'zod';
import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

// ── Helpers ───────────────────────────────────────────────────────────────────

const phone = z
  .string()
  .regex(/^\+?[1-9]\d{7,14}$/, 'Enter a valid phone number (e.g. +15551234567)');

const futureDateTime = z.string().refine(
  (v) => new Date(v) > new Date(),
  'Pickup time must be in the future',
);

// ── Form schemas ──────────────────────────────────────────────────────────────

export const LoginFormSchema = z.object({
  email: z.string().email('Enter a valid email address'),
  password: z.string().min(6, 'Password must be at least 6 characters'),
});

export const RegisterFormSchema = z.object({
  organizationName: z.string().min(2, 'Organization name must be at least 2 characters').max(200),
  facilityName: z.string().min(2, 'Facility name must be at least 2 characters').max(200).optional(),
  facilityAddress: z.string().min(5, 'Enter a full address').max(500).optional(),
  firstName: z.string().min(1, 'First name is required').max(100),
  lastName: z.string().min(1, 'Last name is required').max(100),
  email: z.string().email('Enter a valid email address'),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters')
    .regex(/[A-Z]/, 'Password must contain at least one uppercase letter')
    .regex(/[0-9]/, 'Password must contain at least one number'),
  role: z.enum(['OrgAdmin', 'FacilityAdmin']),
});

export const BookingFormSchema = z.object({
  residentId: z.string().uuid('Select a resident').optional(),
  pickupDate: z.string().min(1, 'Select a pickup date'),
  pickupTime: z.string().regex(/^\d{2}:\d{2}$/, 'Enter a valid time (HH:MM)'),
  pickupAddress: z.string().min(5, 'Enter a pickup address').max(500),
  destinationAddress: z.string().min(5, 'Enter a destination address').max(500),
  transportMode: z.string().min(1, 'Select a transport type'),
}).refine(
  (data) => {
    if (!data.pickupDate || !data.pickupTime) return true;
    const dt = new Date(`${data.pickupDate}T${data.pickupTime}`);
    return dt > new Date();
  },
  { message: 'Pickup time must be in the future', path: ['pickupTime'] },
);

export const ResidentFormSchema = z.object({
  firstName: z.string().min(1, 'First name is required').max(100),
  lastName: z.string().min(1, 'Last name is required').max(100),
  needsWheelchair: z.boolean(),
  needsOxygen: z.boolean(),
  needsStretcher: z.boolean(),
  needsWalker: z.boolean(),
  driverNotes: z.string().max(1000, 'Notes cannot exceed 1000 characters').optional(),
});

export const VendorFormSchema = z.object({
  name: z.string().min(1, 'Vendor name is required').max(200),
  phoneNumber: phone,
  vendorType: z.enum(['Wheelchair', 'Ambulatory']),
  dispatchMethod: z.enum(['SmsNemt', 'SmsTaxi', 'Broker']),
  capabilityTier: z.enum(['Basic', 'Smart']),
});

export const AcceptInviteFormSchema = z.object({
  firstName: z.string().min(1, 'First name is required').max(100),
  lastName: z.string().min(1, 'Last name is required').max(100),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters')
    .regex(/[A-Z]/, 'Password must contain at least one uppercase letter')
    .regex(/[0-9]/, 'Password must contain at least one number'),
});

// ── Angular ValidatorFn factory ───────────────────────────────────────────────
// Usage: control.addValidators(zodValidator(MySchema.shape.fieldName))

export function zodValidator<T>(schema: z.ZodType<T>): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const result = schema.safeParse(control.value);
    if (result.success) return null;
    const message = result.error.errors[0]?.message ?? 'Invalid value';
    return { zodError: message };
  };
}

// ── Full-form Zod parse → Angular errors helper ────────────────────────────────
// Usage in component: applyZodErrors(this.form, BookingFormSchema)

export function getZodFormErrors<T extends z.ZodObject<z.ZodRawShape>>(
  schema: T,
  value: unknown,
): Partial<Record<keyof z.infer<T>, string>> {
  const result = schema.safeParse(value);
  if (result.success) return {};
  const errors: Partial<Record<keyof z.infer<T>, string>> = {};
  for (const issue of result.error.errors) {
    const key = issue.path[0] as keyof z.infer<T>;
    if (key && !errors[key]) errors[key] = issue.message;
  }
  return errors;
}

// ── Exported types ────────────────────────────────────────────────────────────

export type LoginForm        = z.infer<typeof LoginFormSchema>;
export type RegisterForm     = z.infer<typeof RegisterFormSchema>;
export type BookingForm      = z.infer<typeof BookingFormSchema>;
export type ResidentForm     = z.infer<typeof ResidentFormSchema>;
export type VendorForm       = z.infer<typeof VendorFormSchema>;
export type AcceptInviteForm = z.infer<typeof AcceptInviteFormSchema>;
