<script lang="ts">
  import { t } from 'svelte-i18n'
  import { inputModal, closeInputModal } from '../stores'

  let inputValue = ''
  let pending = false
  let inputEl: HTMLInputElement | undefined

  // Use a tick-based approach instead of reactive block to avoid
  // Svelte 4 dependency tracking: assigning inputValue inside a $: block
  // makes inputValue a dependency, so typing re-fires the block and
  // resets inputValue to defaultValue on every keystroke.
  let lastModal: typeof $inputModal = null
  $: if ($inputModal !== lastModal) {
    lastModal = $inputModal
    if ($inputModal) {
      inputValue = $inputModal.defaultValue ?? ''
      pending = false
      setTimeout(() => {
        if (inputEl) {
          inputEl.focus()
          inputEl.select()
        }
      }, 0)
    }
  }

  function handleConfirm() {
    if (pending) return
    const config = $inputModal
    if (!config) return
    const trimmed = inputValue.trim()
    if (!trimmed && !config.allowEmpty) return
    pending = true
    closeInputModal(trimmed)
  }

  function handleCancel() {
    if (pending) return
    closeInputModal(null)
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') {
      e.preventDefault()
      handleConfirm()
    } else if (e.key === 'Escape') {
      e.preventDefault()
      handleCancel()
    }
  }
</script>

{#if $inputModal}
  <div
    class="fixed inset-0 bg-black/40 flex items-center justify-center z-[60]"
    on:click={handleCancel}
    role="presentation"
  >
    <div
      class="bg-white rounded-lg shadow-xl max-w-sm w-full mx-4 dark:bg-gray-800"
      on:click|stopPropagation
      role="dialog"
      aria-modal="true"
      aria-labelledby="input-dialog-title"
    >
      <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700">
        <h2 id="input-dialog-title" class="text-sm font-semibold text-gray-900 dark:text-gray-100">
          {$inputModal.title}
        </h2>
      </div>

      <div class="px-5 py-4">
        {#if $inputModal.message}
          <p class="text-xs text-gray-600 dark:text-gray-300 leading-relaxed mb-3">
            {$inputModal.message}
          </p>
        {/if}
        <input
          bind:this={inputEl}
          bind:value={inputValue}
          on:keydown={handleKeydown}
          placeholder={$inputModal.placeholder ?? ''}
          maxlength={$inputModal.maxLength ?? 255}
          class="w-full px-3 py-2 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100 dark:placeholder-gray-400"
        />
      </div>

      <div class="px-5 py-3 border-t border-gray-200 flex gap-2 justify-end dark:border-gray-700">
        <button
          on:click={handleCancel}
          class="px-4 py-1.5 text-xs text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
        >
          {$t('buttons.cancel')}
        </button>
        <button
          on:click={handleConfirm}
          class="px-4 py-1.5 text-xs text-white rounded-md transition bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600"
        >
          {$t('buttons.save')}
        </button>
      </div>
    </div>
  </div>
{/if}
