<script lang="ts">
  import { t } from 'svelte-i18n'
  import { modal, closeModal } from '../stores'

  let pending = false

  async function handleConfirm() {
    if (pending) return
    const config = $modal
    pending = true
    closeModal()
    try {
      await config?.onConfirm?.()
    } finally {
      pending = false
    }
  }

  function handleCancel() {
    if (pending) return
    closeModal()
  }
</script>

{#if $modal}
  <div
    class="fixed inset-0 bg-black/40 flex items-center justify-center z-[60]"
    on:click={handleCancel}
    on:keydown={(e) => { if (e.key === 'Escape') handleCancel() }}
    role="presentation"
  >
    <div
      class="bg-white rounded-lg shadow-xl max-w-sm w-full mx-4 dark:bg-gray-800"
      on:click|stopPropagation
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="modal-title"
      aria-describedby="modal-message"
    >
      <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700">
        <h2 id="modal-title" class="text-sm font-semibold text-gray-900 dark:text-gray-100">
          {$modal.title}
        </h2>
      </div>

      <div class="px-5 py-4">
        <p id="modal-message" class="text-xs text-gray-600 dark:text-gray-300 leading-relaxed">
          {$modal.message}
        </p>
      </div>

      <div class="px-5 py-3 border-t border-gray-200 flex gap-2 justify-end dark:border-gray-700">
        <button
          on:click={handleCancel}
          class="px-4 py-1.5 text-xs text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
        >
          {$modal.cancelLabel || $t('buttons.cancel')}
        </button>
        <button
          on:click={handleConfirm}
          class="px-4 py-1.5 text-xs text-white rounded-md transition {$modal.variant === 'danger'
            ? 'bg-red-600 hover:bg-red-700 dark:bg-red-500 dark:hover:bg-red-600'
            : 'bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600'}"
        >
          {$modal.confirmLabel || $t('buttons.save')}
        </button>
      </div>
    </div>
  </div>
{/if}
