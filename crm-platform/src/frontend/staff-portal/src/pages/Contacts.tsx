import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { zodResolver } from '@hookform/resolvers/zod';
import { useContacts, useCreateContact, useDeleteContact } from '../hooks/useContacts';
import { Table, Button, Input, Modal, ConfirmModal } from '../components/ui';
import type { Column } from '../components/ui';
import type { Contact, CreateContactRequest, PaginationParams } from '../types';

const createContactSchema = z.object({
  firstName: z.string().min(1),
  lastName: z.string().min(1),
  email: z.string().email(),
  phone: z.string().optional(),
  jobTitle: z.string().optional(),
  accountId: z.string().optional(),
});

type CreateContactFormValues = z.infer<typeof createContactSchema>;

export function Contacts() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [params, setParams] = useState<PaginationParams>({ page: 1, pageSize: 20 });
  const [createOpen, setCreateOpen] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data, isLoading } = useContacts(params);
  const createContact = useCreateContact();
  const deleteContact = useDeleteContact();

  const { register, handleSubmit, reset, formState: { errors } } = useForm<CreateContactFormValues>({
    resolver: zodResolver(createContactSchema),
  });

  const onCreateSubmit = (values: CreateContactFormValues) => {
    const req: CreateContactRequest = values;
    createContact.mutate(req, {
      onSuccess: () => { setCreateOpen(false); reset(); },
    });
  };

  const columns: Column<Contact>[] = [
    {
      key: 'firstName',
      header: t('contacts.name'),
      render: (row) => `${row.firstName} ${row.lastName}`,
    },
    { key: 'email', header: t('common.email') },
    { key: 'jobTitle', header: t('contacts.jobTitle') },
    { key: 'phone', header: t('common.phone') },
    {
      key: 'actions',
      header: t('common.actions'),
      render: (row) => (
        <button
          onClick={(e) => { e.stopPropagation(); setDeleteId(row.id); }}
          className="text-xs text-danger-500 hover:text-danger-700"
        >
          {t('common.delete')}
        </button>
      ),
    },
  ];

  return (
    <div className="p-6">
      <div className="mb-6 flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">{t('contacts.title')}</h1>
        <Button onClick={() => { setCreateOpen(true); }}>
          {t('contacts.newContact')}
        </Button>
      </div>

      <Table
        columns={columns}
        data={data?.items ?? []}
        loading={isLoading}
        emptyMessage={t('common.noResults')}
        onRowClick={(row) => { void navigate(`/contacts/${row.id}`); }}
      />

      {data && data.totalPages > 1 && (
        <div className="mt-4 flex justify-end gap-2">
          <Button size="sm" variant="secondary" disabled={params.page <= 1}
            onClick={() => { setParams((p) => ({ ...p, page: p.page - 1 })); }}>
            {t('common.previous')}
          </Button>
          <Button size="sm" variant="secondary" disabled={params.page >= data.totalPages}
            onClick={() => { setParams((p) => ({ ...p, page: p.page + 1 })); }}>
            {t('common.next')}
          </Button>
        </div>
      )}

      <Modal
        open={createOpen}
        onClose={() => { setCreateOpen(false); reset(); }}
        title={t('contacts.newContact')}
        footer={
          <>
            <Button variant="secondary" onClick={() => { setCreateOpen(false); reset(); }}>
              {t('common.cancel')}
            </Button>
            <Button form="create-contact-form" type="submit" loading={createContact.isPending}>
              {t('common.create')}
            </Button>
          </>
        }
      >
        <form
          id="create-contact-form"
          onSubmit={(e) => { void handleSubmit(onCreateSubmit)(e); }}
          className="space-y-4"
        >
          <div className="grid grid-cols-2 gap-4">
            <Input label={t('contacts.firstName')} required {...register('firstName')} error={errors.firstName?.message} />
            <Input label={t('contacts.lastName')} required {...register('lastName')} error={errors.lastName?.message} />
          </div>
          <Input label={t('common.email')} type="email" required {...register('email')} error={errors.email?.message} />
          <Input label={t('common.phone')} {...register('phone')} />
          <Input label={t('contacts.jobTitle')} {...register('jobTitle')} />
        </form>
      </Modal>

      <ConfirmModal
        open={deleteId !== null}
        onClose={() => { setDeleteId(null); }}
        onConfirm={() => {
          if (deleteId) deleteContact.mutate(deleteId, { onSuccess: () => { setDeleteId(null); } });
        }}
        title={t('contacts.deleteContact')}
        message={t('contacts.deleteConfirm')}
        confirmLabel={t('common.delete')}
        loading={deleteContact.isPending}
      />
    </div>
  );
}
