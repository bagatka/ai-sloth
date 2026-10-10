// Where this app lives and where its host's API is. The host fills both into the page it serves
// (WebApp.cs in the WebApi): the page's <base> is the app's address, and the aisloth-api meta element
// names the API's. In development, Vite serves the page as written, and the API is on the same
// origin through Vite's proxy.

/** The app's path on its address, such as "/app/", ending in a slash. */
export const appBase = new URL(document.baseURI).pathname

/** The host's API address, ending in a slash. */
export const apiUrl = new URL(
  document.querySelector<HTMLMetaElement>('meta[name="aisloth-api"]')
    ?.content || "/",
  location.href
).href

/** Whether the API is on another origin, so requests carry the session cookie across origins. */
export const apiElsewhere = new URL(apiUrl).origin !== location.origin

/**
 * How this browser names itself in the person's list of signed-in devices, such as "Firefox on
 * macOS".
 */
export function deviceName(): string {
  const agent = navigator.userAgent
  const browser = /Edg\//.test(agent)
    ? "Edge"
    : /Firefox\//.test(agent)
      ? "Firefox"
      : /Chrome\//.test(agent)
        ? "Chrome"
        : /Safari\//.test(agent)
          ? "Safari"
          : "A browser"
  const system = /iPhone|iPad/.test(agent)
    ? "iOS"
    : /Android/.test(agent)
      ? "Android"
      : /Mac OS X/.test(agent)
        ? "macOS"
        : /Windows/.test(agent)
          ? "Windows"
          : /Linux/.test(agent)
            ? "Linux"
            : null
  return system ? `${browser} on ${system}` : browser
}
