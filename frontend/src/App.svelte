<script lang="ts">
  import { onMount } from 'svelte'
  import Variables from './lib/components/Variables.svelte'
  import { variables, loading, error } from './lib/stores'
  import { listVariables } from './lib/api'

  onMount(async () => {
    await listVariables()
  })
</script>

<main class="min-h-screen bg-gray-50">
  <header class="bg-white border-b border-gray-200 px-6 py-4">
    <h1 class="text-2xl font-bold text-gray-900">Env Manager</h1>
    <p class="text-gray-600 text-sm mt-1">Manage Windows environment variables</p>
  </header>

  <div class="container mx-auto px-6 py-8">
    {#if $error}
      <div class="bg-red-50 border border-red-200 text-red-800 px-4 py-3 rounded mb-4">
        {$error}
      </div>
    {/if}

    {#if $loading}
      <div class="flex justify-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-gray-900"></div>
      </div>
    {:else}
      <Variables />
    {/if}
  </div>
</main>

<style global>
  @import 'tailwindcss/base';
  @import 'tailwindcss/components';
  @import 'tailwindcss/utilities';

  :global(body) {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen,
      Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
    margin: 0;
    padding: 0;
  }
</style>
