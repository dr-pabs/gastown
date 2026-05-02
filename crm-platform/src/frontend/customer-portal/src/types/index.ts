// ── Shared ────────────────────────────────────────────────────────────────────

export interface PaginationParams {
  page: number;
  pageSize: number;
  search?: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

// ── CS&S Cases ────────────────────────────────────────────────────────────────

export type CaseStatus = 'Open' | 'InProgress' | 'WaitingOnCustomer' | 'Resolved' | 'Closed';
export type CasePriority = 'Low' | 'Medium' | 'High' | 'Critical';
export type SentimentLabel = 'Positive' | 'Neutral' | 'Negative' | 'Mixed';

export interface Case {
  id: string;
  tenantId: string;
  contactId?: string;
  accountId?: string;
  ownerId?: string;
  subject: string;
  description?: string;
  status: CaseStatus;
  priority: CasePriority;
  category?: string;
  slaBreached: boolean;
  slaDueAt?: string;
  sentiment?: SentimentLabel;
  resolvedAt?: string;
  isDeleted: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CaseComment {
  id: string;
  caseId: string;
  authorId: string;
  authorName?: string;
  body: string;
  isInternal: boolean;
  createdAt: string;
}

export interface CreateCaseRequest {
  subject: string;
  description?: string;
  priority: CasePriority;
  category?: string;
}

export interface UpdateCaseRequest {
  subject?: string;
  description?: string;
  status?: CaseStatus;
  priority?: CasePriority;
  category?: string;
}

export interface AddCaseCommentRequest {
  body: string;
  isInternal?: boolean;
}
