// ─────────────────────────────────────────────────────────────────────────────
// Shared TypeScript types for the Staff Portal.
// All types mirror the backend DTO shapes.
// ─────────────────────────────────────────────────────────────────────────────

// ── Pagination ────────────────────────────────────────────────────────────────

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface PaginationParams {
  page: number;
  pageSize: number;
  search?: string;
  sortBy?: string;
  sortDesc?: boolean;
}

// ── Auth / User ───────────────────────────────────────────────────────────────

export interface UserProfile {
  id: string;
  tenantId: string;
  email: string;
  displayName: string;
  roles: string[];
  avatarUrl?: string;
}

// ── SFA — Leads ───────────────────────────────────────────────────────────────

export type LeadStage =
  | 'New'
  | 'Contacted'
  | 'Qualified'
  | 'Converted'
  | 'Disqualified';

export interface Lead {
  id: string;
  tenantId: string;
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  company?: string;
  jobTitle?: string;
  source?: string;
  stage: LeadStage;
  score?: number;
  nextBestAction?: string;
  ownerId?: string;
  ownerName?: string;
  notes?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateLeadRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  company?: string;
  jobTitle?: string;
  source?: string;
  notes?: string;
}

export interface UpdateLeadRequest {
  firstName?: string;
  lastName?: string;
  email?: string;
  phone?: string;
  company?: string;
  jobTitle?: string;
  source?: string;
  stage?: LeadStage;
  notes?: string;
}

// ── SFA — Contacts ────────────────────────────────────────────────────────────

export interface Contact {
  id: string;
  tenantId: string;
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  jobTitle?: string;
  accountId?: string;
  accountName?: string;
  ownerId?: string;
  ownerName?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateContactRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  jobTitle?: string;
  accountId?: string;
}

export interface UpdateContactRequest {
  firstName?: string;
  lastName?: string;
  email?: string;
  phone?: string;
  jobTitle?: string;
  accountId?: string;
}

// ── SFA — Accounts ────────────────────────────────────────────────────────────

export interface Account {
  id: string;
  tenantId: string;
  name: string;
  website?: string;
  industry?: string;
  employees?: number;
  annualRevenue?: number;
  phone?: string;
  billingAddress?: Address;
  ownerId?: string;
  ownerName?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Address {
  street?: string;
  city?: string;
  state?: string;
  postCode?: string;
  country?: string;
}

export interface CreateAccountRequest {
  name: string;
  website?: string;
  industry?: string;
  employees?: number;
  annualRevenue?: number;
  phone?: string;
}

export interface UpdateAccountRequest {
  name?: string;
  website?: string;
  industry?: string;
  employees?: number;
  annualRevenue?: number;
  phone?: string;
}

// ── SFA — Opportunities ───────────────────────────────────────────────────────

export type OpportunityStage =
  | 'Prospecting'
  | 'Qualification'
  | 'Proposal'
  | 'Negotiation'
  | 'ClosedWon'
  | 'ClosedLost';

export interface Opportunity {
  id: string;
  tenantId: string;
  name: string;
  accountId: string;
  accountName?: string;
  contactId?: string;
  contactName?: string;
  stage: OpportunityStage;
  amount?: number;
  probability?: number;
  closeDate?: string;
  ownerId?: string;
  ownerName?: string;
  description?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateOpportunityRequest {
  name: string;
  accountId: string;
  contactId?: string;
  stage: OpportunityStage;
  amount?: number;
  probability?: number;
  closeDate?: string;
  description?: string;
}

export interface UpdateOpportunityRequest {
  name?: string;
  accountId?: string;
  contactId?: string;
  stage?: OpportunityStage;
  amount?: number;
  probability?: number;
  closeDate?: string;
  description?: string;
}

// ── CS&S — Cases ─────────────────────────────────────────────────────────────

export type CaseStatus = 'Open' | 'Pending' | 'Resolved' | 'Closed';
export type CasePriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type SentimentLabel = 'Positive' | 'Neutral' | 'Negative' | 'Mixed';

export interface Case {
  id: string;
  tenantId: string;
  caseNumber: string;
  subject: string;
  description?: string;
  status: CaseStatus;
  priority: CasePriority;
  category?: string;
  contactId?: string;
  contactName?: string;
  accountId?: string;
  accountName?: string;
  ownerId?: string;
  ownerName?: string;
  aiSummary?: string;
  sentiment?: SentimentLabel;
  slaBreached: boolean;
  slaDueAt?: string;
  resolvedAt?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CaseComment {
  id: string;
  caseId: string;
  authorId: string;
  authorName: string;
  body: string;
  isInternal: boolean;
  createdAt: string;
}

export interface CreateCaseRequest {
  subject: string;
  description?: string;
  priority: CasePriority;
  category?: string;
  contactId?: string;
  accountId?: string;
}

export interface UpdateCaseRequest {
  subject?: string;
  description?: string;
  status?: CaseStatus;
  priority?: CasePriority;
  category?: string;
  contactId?: string;
  accountId?: string;
  ownerId?: string;
}

export interface AddCaseCommentRequest {
  body: string;
  isInternal: boolean;
}

// ── Marketing — Campaigns ─────────────────────────────────────────────────────

export type CampaignStatus = 'Draft' | 'Active' | 'Paused' | 'Completed' | 'Archived';

export interface Campaign {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  status: CampaignStatus;
  channel?: string;
  startDate?: string;
  endDate?: string;
  budget?: number;
  impressions: number;
  clicks: number;
  conversions: number;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Marketing — Journeys ──────────────────────────────────────────────────────

export type JourneyStatus = 'Draft' | 'Active' | 'Paused' | 'Archived';

export interface Journey {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  status: JourneyStatus;
  enrollmentCount: number;
  activeEnrollments: number;
  completedEnrollments: number;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Notifications ─────────────────────────────────────────────────────────────

export type NotificationChannel = 'InApp' | 'Email' | 'Sms' | 'Teams';

export interface Notification {
  id: string;
  tenantId: string;
  recipientUserId: string;
  channel: NotificationChannel;
  subject: string;
  body: string;
  isRead: boolean;
  entityType?: string;
  entityId?: string;
  sentAt?: string;
  readAt?: string;
  createdAt: string;
}

// ── AI ────────────────────────────────────────────────────────────────────────

export type AiJobStatus = 'Queued' | 'InProgress' | 'Succeeded' | 'Failed' | 'Abandoned';
export type CapabilityType =
  | 'Summarisation'
  | 'SentimentAnalysis'
  | 'LeadScoring'
  | 'NextBestAction'
  | 'DraftGeneration'
  | 'JourneyPersonalisation'
  | 'Forecasting'
  | 'ChurnPrediction'
  | 'ContentGeneration';

export interface AiJob {
  id: string;
  tenantId: string;
  capability: CapabilityType;
  status: AiJobStatus;
  entityType: string;
  entityId: string;
  inputPayload: string;
  errorReason?: string;
  attemptCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface AiResult {
  id: string;
  tenantId: string;
  jobId: string;
  capability: CapabilityType;
  entityType: string;
  entityId: string;
  outputText?: string;
  structuredJson?: string;
  modelId: string;
  promptTokens: number;
  completionTokens: number;
  createdAt: string;
}

export interface PromptTemplate {
  id: string;
  tenantId?: string;
  capability: CapabilityType;
  useCase: string;
  name: string;
  templateBody: string;
  isSystemDefault: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SyncAiRequest {
  capability: CapabilityType;
  entityType: string;
  entityId: string;
  inputPayload: string;
}

export interface AsyncAiRequest {
  capability: CapabilityType;
  entityType: string;
  entityId: string;
  inputPayload: string;
}

// ── Analytics ─────────────────────────────────────────────────────────────────

export interface AnalyticsMetric {
  metricName: string;
  value: number;
  delta?: number;
  period: string;
}

export interface AnalyticsReport {
  id: string;
  tenantId: string;
  name: string;
  description?: string;
  createdAt: string;
}
