<script lang="ts">
  import { t } from 'svelte-i18n'
  import { frontendLog } from '../settingsStore'
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
    class="fixed inset-0 bg-background/40 flex items-center justify-center z-[60]"
    on:click={handleCancel}
    on:keydown={(e) => { if (e.key === 'Escape') handleCancel() }}
    role="presentation"
  >
    <div
      class="bg-card rounded-lg shadow-xl max-w-sm w-full mx-4"
      on:click|stopPropagation
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="modal-title"
      aria-describedby="modal-message"
    >
      <div class="px-5 py-4 border-b border-border">
        <h2 id="modal-title" class="text-sm font-semibold text-foreground">
          {$modal.title}
        </h2>
      </div>

      <div class="px-5 py-4">
        <p id="modal-message" class="text-xs text-muted-foreground text-foreground/80 leading-relaxed">
          {$modal.message}
        </p>
      </div>

      <div class="px-5 py-3 border-t border-border flex gap-2 justify-end">
        <button
          on:click={handleCancel}
          class="px-4 py-1.5 text-xs text-foreground/80 border border-border rounded-md hover:bg-muted/20 transition text-foreground border-border/80 hover:bg-accent"
        >
          {$modal.cancelLabel || $t('buttons.cancel')}
        </button>
        <button
          on:click={handleConfirm}
          class="px-4 py-1.5 text-xs text-primary-foreground rounded-md transition {$modal.variant === 'danger'
            ? 'bg-destructive hover:bg-red-700 bg-destructive hover:bg-destructive'
            : 'bg-primary hover:bg-primary bg-primary/80 hover:bg-primary'}"
        >
          {$modal.confirmLabel || $t('buttons.save')}
        </button>
      </div>
    </div>
  </div>
{/if}
