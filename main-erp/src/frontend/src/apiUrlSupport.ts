export function isApiUrl(value: string, apiRoot: string, configuredApiRoot: string) {
  return value.startsWith('/api')
    || value.startsWith(apiRoot)
    || (configuredApiRoot.startsWith('http') && value.startsWith(configuredApiRoot))
}

export function normalizeDevelopmentApiUrl(
  value: string,
  apiRoot: string,
  configuredApiRoot: string,
  development: boolean,
) {
  if (!development || !configuredApiRoot.startsWith('http') || !value.startsWith(configuredApiRoot)) {
    return value
  }

  return `${apiRoot}${value.slice(configuredApiRoot.length)}`
}
