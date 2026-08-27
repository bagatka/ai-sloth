let env_file = ($env.FILE_PWD | path join ".env")

let account_id = (input "Cloudflare account ID: " | str trim)
let api_token = (input --suppress-output "Cloudflare API token: " | str trim)
print ""

if ($account_id | is-empty) or ($api_token | is-empty) {
  error make { msg: "Account ID and API token are required" }
}

let credentials = {
  CLOUDFLARE_ACCOUNT_ID: $account_id
  CLOUDFLARE_API_TOKEN: $api_token
}
let auth = with-env $credentials {
  wrangler whoami --account $account_id
} | complete

if $auth.exit_code != 0 {
  print --stderr $auth.stderr
  error make { msg: "Cloudflare credentials are invalid" }
}

let d1 = with-env $credentials {
  wrangler d1 list --json
} | complete
if $d1.exit_code != 0 {
  print --stderr $d1.stderr
  error make { msg: "Cloudflare token does not have D1 access" }
}

let r2 = with-env $credentials {
  wrangler r2 bucket list
} | complete
if $r2.exit_code != 0 {
  print --stderr $r2.stderr
  error make { msg: "Cloudflare token does not have R2 access" }
}

print "Cloudflare credentials validated"
let _ = (umask rwx------)
$"CLOUDFLARE_ACCOUNT_ID=($account_id)\nCLOUDFLARE_API_TOKEN=($api_token)\n" | save --force $env_file
