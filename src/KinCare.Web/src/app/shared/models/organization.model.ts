export interface Organization {
  id: string;
  name: string;
  planTier: 'Starter' | 'Professional' | 'Enterprise';
  isActive: boolean;
  billingEmail: string;
  createdAt: string;
}
