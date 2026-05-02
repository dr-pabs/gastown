import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type { Opportunity, OpportunityStage } from '../../types';
import { Badge } from '../ui';

interface OpportunityBoardProps {
  opportunities: Opportunity[];
  loading?: boolean;
  onCardClick?: (opportunity: Opportunity) => void;
}

const STAGES: OpportunityStage[] = [
  'Prospecting',
  'Qualification',
  'Proposal',
  'Negotiation',
  'ClosedWon',
  'ClosedLost',
];

function formatCurrency(amount?: number) {
  if (amount === undefined) return '';
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(amount);
}

export function OpportunityBoard({ opportunities, loading, onCardClick }: OpportunityBoardProps) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  if (loading) {
    return (
      <div className="flex h-64 items-center justify-center">
        <span className="text-sm text-gray-400">{t('common.loading')}</span>
      </div>
    );
  }

  const byStage = STAGES.map((stage) => ({
    stage,
    items: opportunities.filter((o) => o.stage === stage),
  }));

  return (
    <div className="flex gap-4 overflow-x-auto pb-4">
      {byStage.map(({ stage, items }) => (
        <div key={stage} className="min-w-[240px] flex-shrink-0">
          <div className="mb-2 flex items-center justify-between rounded-t-md bg-gray-100 px-3 py-2">
            <span className="text-sm font-semibold text-gray-700">
              {t(`opportunities.stages.${stage.charAt(0).toLowerCase() + stage.slice(1)}`)}
            </span>
            <Badge label={String(items.length)} variant="default" />
          </div>

          <div className="space-y-2">
            {items.map((opp) => (
              <div
                key={opp.id}
                onClick={() => {
                  if (onCardClick) {
                    onCardClick(opp);
                  } else {
                    void navigate(`/opportunities/${opp.id}`);
                  }
                }}
                className="cursor-pointer rounded-md border bg-white p-3 shadow-sm hover:shadow-md transition-shadow"
              >
                <p className="text-sm font-medium text-gray-900 line-clamp-2">{opp.name}</p>
                {opp.accountName && (
                  <p className="mt-0.5 text-xs text-gray-500">{opp.accountName}</p>
                )}
                <div className="mt-2 flex items-center justify-between">
                  {opp.amount !== undefined && (
                    <span className="text-sm font-semibold text-primary-600">
                      {formatCurrency(opp.amount)}
                    </span>
                  )}
                  {opp.probability !== undefined && (
                    <span className="text-xs text-gray-400">{opp.probability}%</span>
                  )}
                </div>
              </div>
            ))}

            {items.length === 0 && (
              <div className="rounded-md border border-dashed p-4 text-center text-xs text-gray-400">
                {t('common.noResults')}
              </div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
