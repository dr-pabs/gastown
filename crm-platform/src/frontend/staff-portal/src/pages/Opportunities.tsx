import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useOpportunities } from '../hooks/useOpportunities';
import { Button } from '../components/ui';
import { OpportunityBoard } from '../components/features/OpportunityBoard';
import type { PaginationParams } from '../types';

export function Opportunities() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [view, setView] = useState<'kanban' | 'list'>('kanban');
  const [params] = useState<PaginationParams>({ page: 1, pageSize: 100 });

  const { data, isLoading } = useOpportunities(params);
  const opportunities = data?.items ?? [];

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b border-gray-200 bg-white px-6 py-4">
        <h1 className="text-2xl font-bold text-gray-900">{t('opportunities.title')}</h1>
        <div className="flex items-center gap-3">
          <div className="flex rounded-md border border-gray-200 overflow-hidden">
            <button
              onClick={() => { setView('kanban'); }}
              className={`px-3 py-1.5 text-sm font-medium transition-colors ${
                view === 'kanban'
                  ? 'bg-primary-600 text-white'
                  : 'bg-white text-gray-600 hover:bg-gray-50'
              }`}
            >
              {t('opportunities.kanban')}
            </button>
            <button
              onClick={() => { setView('list'); }}
              className={`px-3 py-1.5 text-sm font-medium transition-colors ${
                view === 'list'
                  ? 'bg-primary-600 text-white'
                  : 'bg-white text-gray-600 hover:bg-gray-50'
              }`}
            >
              {t('common.list')}
            </button>
          </div>
          <Button onClick={() => { void navigate('/opportunities/new'); }}>
            {t('opportunities.newOpportunity')}
          </Button>
        </div>
      </div>

      <div className="flex-1 overflow-hidden">
        {view === 'kanban' ? (
          <OpportunityBoard
            opportunities={opportunities}
            loading={isLoading}
            onCardClick={(opp) => { void navigate(`/opportunities/${opp.id}`); }}
          />
        ) : (
          <div className="p-6">
            <div className="overflow-x-auto rounded-lg border border-gray-200 bg-white">
              <table className="min-w-full divide-y divide-gray-200 text-sm">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-3 text-left font-medium text-gray-600">{t('opportunities.title_singular')}</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-600">{t('opportunities.stage')}</th>
                    <th className="px-4 py-3 text-right font-medium text-gray-600">{t('opportunities.amount')}</th>
                    <th className="px-4 py-3 text-right font-medium text-gray-600">{t('opportunities.probability')}</th>
                    <th className="px-4 py-3 text-left font-medium text-gray-600">{t('opportunities.closeDate')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {isLoading ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-8 text-center text-gray-400">
                        {t('common.loading')}
                      </td>
                    </tr>
                  ) : opportunities.map((opp) => (
                    <tr
                      key={opp.id}
                      onClick={() => { void navigate(`/opportunities/${opp.id}`); }}
                      className="cursor-pointer hover:bg-gray-50"
                    >
                      <td className="px-4 py-3 font-medium text-gray-900">{opp.title}</td>
                      <td className="px-4 py-3 text-gray-600">{opp.stage}</td>
                      <td className="px-4 py-3 text-right text-gray-900">
                        {opp.amount !== undefined
                          ? new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 }).format(opp.amount)
                          : '—'
                        }
                      </td>
                      <td className="px-4 py-3 text-right text-gray-600">
                        {opp.probability !== undefined ? `${opp.probability}%` : '—'}
                      </td>
                      <td className="px-4 py-3 text-gray-600">
                        {opp.closeDate ? new Date(opp.closeDate).toLocaleDateString() : '—'}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
