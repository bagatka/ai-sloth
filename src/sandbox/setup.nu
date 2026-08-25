let env_file = ($env.FILE_PWD | path join ".env")

if ($env_file | path exists) {
  error make { msg: $"($env_file) already exists" }
}

let account_id = (input "Cloudflare account ID: " | str trim)
let api_token = (input --suppress-output "Cloudflare API token: " | str trim)
print ""

if ($account_id | is-empty) or ($api_token | is-empty) {
  error make { msg: "Account ID and API token are required" }
}

let auth = with-env {
  CLOUDFLARE_ACCOUNT_ID: $account_id
  CLOUDFLARE_API_TOKEN: $api_token
} {
  wrangler whoami --account $account_id
} | complete

if $auth.exit_code != 0 {
  print --stderr $auth.stderr
  error make { msg: "Cloudflare credentials are invalid" }
}

print $auth.stdout
let _ = (umask rwx------)
$"CLOUDFLARE_ACCOUNT_ID=($account_id)\nCLOUDFLARE_API_TOKEN=($api_token)\n" | save $env_file
