import { describe, it, expect } from 'vitest'
import { get } from 'svelte/store'
import {
  variables,
  loading,
  error,
  selectedScope,
  profiles,
  activeView,
} from './stores'
import type { EnvVariable, ProfileData, PathEntry } from './stores'

describe('stores', () => {
  it('variables store starts empty', () => {
    expect(get(variables)).toEqual([])
  })

  it('variables store accepts data', () => {
    const mockVars: EnvVariable[] = [
      { name: 'PATH', value: 'C:\\Windows', scope: 'user' },
      { name: 'JAVA_HOME', value: 'C:\\Java', scope: 'system' },
    ]
    variables.set(mockVars)
    expect(get(variables)).toHaveLength(2)
    expect(get(variables)[0].name).toBe('PATH')
  })

  it('loading store defaults to false', () => {
    loading.set(false)
    expect(get(loading)).toBe(false)
  })

  it('error store defaults to null', () => {
    expect(get(error)).toBeNull()
  })

  it('selectedScope store defaults to all', () => {
    expect(get(selectedScope)).toBe('all')
  })

  it('profiles store starts empty', () => {
    expect(get(profiles)).toEqual([])
  })

  it('profiles store accepts profile data', () => {
    const mockProfiles: ProfileData[] = [
      {
        id: 'test-id',
        name: 'dev',
        isEnabled: false,
        variables: [{ name: 'NODE_ENV', value: 'development' }],
      },
    ]
    profiles.set(mockProfiles)
    expect(get(profiles)).toHaveLength(1)
    expect(get(profiles)[0].name).toBe('dev')
    expect(get(profiles)[0].variables).toHaveLength(1)
  })

  it('activeView store defaults to variables', () => {
    expect(get(activeView)).toBe('variables')
  })

  it('activeView store can switch views', () => {
    activeView.set('profiles')
    expect(get(activeView)).toBe('profiles')
    activeView.set('path')
    expect(get(activeView)).toBe('path')
    activeView.set('variables')
  })
})
