import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { Lead } from '../../types';
import { Badge } from '../ui';

interface LeadCardProps {
  lead: Lead;
}

function getStageVariant(stage: Lead['stage']) {
  switch (stage) {
    case 'New':       return 'info' as const;
    case 'Contacted': return 'default' as const;
    case 'Qualified': return 'success' as const;
    case 'Converted': return 'success' as const;
    case 'Disqualified': return 'danger' as const;
    default:          return 'default' as const;
  }
}

export function LeadCard({ lead }: LeadCardProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <div
      onClick={() => { void navigate(`/leads/${lead.id}`); }}
      className="cursor-pointer rounded-lg border bg-white p-4 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="mb-2 flex items-center justify-between">
        <span className="font-medium text-gray-900">
          {lead.firstName} {lead.lastName}
        </span>
        <Badge label={lead.stage} variant={getStageVariant(lead.stage)} />
      </div>

      {lead.company && (
        <p className="text-sm text-gray-500">{lead.company}</p>
      )}

      {lead.score !== undefined && (
        <div className="mt-2 flex items-center gap-2">
          <div className="h-1.5 flex-1 rounded-full bg-gray-200">
            <div
              className="h-1.5 rounded-full bg-primary-500"
              style={{ width: `${lead.score}%` }}
            />
          </div>
          <span className="text-xs text-gray-500">{lead.score}</span>
        </div>
      )}

      {lead.nextBestAction && (
        <p className="mt-2 rounded bg-primary-50 px-2 py-1 text-xs text-primary-700">
          {t('leads.nextBestAction')}: {lead.nextBestAction}
        </p>
      )}
    </div>
  );
}
