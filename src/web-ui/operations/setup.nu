def normalize-api-url [value: string] {
  let normalized = ($value | str trim | str trim --right --char "/")
  let parsed = try {
    $normalized | url parse
  } catch {
    error make { msg: "API URL must be an absolute HTTP or HTTPS URL" }
  }

  if (
    $parsed.scheme not-in ["http" "https"]
    or ($parsed.host | is-empty)
    or not ($parsed.username | is-empty)
    or not ($parsed.password | is-empty)
    or $parsed.path not-in ["" "/"]
    or not ($parsed.query | is-empty)
    or not ($parsed.fragment | is-empty)
  ) {
    error make { msg: "API URL must contain only an HTTP or HTTPS origin" }
  }

  $normalized
}

def main [api_url?: string] {
  let selected = if $api_url == null {
    input --default "http://localhost:8787" "API URL: "
  } else {
    $api_url
  }
  let api_url = normalize-api-url $selected
  let env_file = ($env.FILE_PWD | path join "../.env.local")

  $"API_PROXY_TARGET=($api_url)\n" | save --force $env_file
  print $"Web UI API proxy configured for ($api_url)"
}
