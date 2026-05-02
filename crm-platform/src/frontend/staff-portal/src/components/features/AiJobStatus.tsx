import { useTranslation } from 'react-i18next';
import { useAiJob } from '../../hooks/useAiJobs';
import { Badge } from '../ui';
import { Spinner } from '../ui';
import type { AiJobStatus } from '../../types';

interface AiJobStatusProps {
  jobId: string;
}

function getStatusVariant(status: AiJobStatus) {
  switch (status) {
    case 'Succeeded': return 'success' as const;
    case 'Failed':
    case 'Abandoned':  return 'danger' as const;
    case 'InProgress': return 'info' as const;
    default:           return 'default' as const;
  }
}

export function AiJobStatus({ jobId }: AiJobStatusProps) {
  const { t } = useTranslation();
  const { data: job, isLoading } = useAiJob(jobId);

  if (isLoading) return <Spinner size="sm" />;
  if (!job) return null;

  const isTerminal = job.status === 'Succeeded' || job.status === 'Failed' || job.status === 'Abandoned';

  return (
    <div className="flex items-center gap-2">
      {!isTerminal && <Spinner size="sm" />}
      <Badge
        label={t(`ai.status.${job.status.charAt(0).toLowerCase() + job.status.slice(1)}`)}
        variant={getStatusVariant(job.status)}
      />
      {job.errorReason && (
        <span className="text-xs text-danger-700">{job.errorReason}</span>
      )}
    </div>
  );
}
