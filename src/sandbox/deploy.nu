def fail-step [description: string, result: record] {
  if not ($result.stdout | is-empty) {
    print $result.stdout
  }
  if not ($result.stderr | is-empty) {
    print --stderr $result.stderr
  }
  error make { msg: $"($description) failed" }
}

def run-step [description: string, action: closure] {
  print $"($description)..."
  let result = (do $action | complete)

  if $result.exit_code != 0 {
    fail-step $description $result
  }
  if not ($result.stdout | is-empty) {
    print $result.stdout
  }
}

def read-wrangler-config [path: string] {
  let result = (
    bun -e 'const config = await import(process.argv[1]); process.stdout.write(JSON.stringify(config.default));' $path
    | complete
  )
  if $result.exit_code != 0 {
    fail-step "Reading Wrangler configuration" $result
  }
  $result.stdout | from json
}

def ensure-d1-database [name: string] {
  let result = (wrangler d1 list --json | complete)
  if $result.exit_code != 0 {
    fail-step "Checking D1 database" $result
  }

  let exists = (
    $result.stdout
    | from json
    | any {|database| $database.name == $name }
  )
  if not $exists {
    run-step $"Creating D1 database '($name)'" {
      wrangler d1 create $name
    }
  }
}

def ensure-r2-bucket [name: string] {
  let result = (wrangler r2 bucket info $name --json | complete)
  if $result.exit_code == 0 {
    return
  }
  if not ($result.stderr | str contains "code: 10006") {
    fail-step "Checking R2 bucket" $result
  }

  run-step $"Creating R2 bucket '($name)'" {
    wrangler r2 bucket create $name
  }
}

let env_file = ($env.FILE_PWD | path join ".env")
if not ($env_file | path exists) {
  error make { msg: "Cloudflare credentials are missing; run 'bun run setup' first" }
}

open --raw $env_file
| lines
| parse "{key}={value}"
| transpose --header-row --as-record
| load-env

if (
  ($env.CLOUDFLARE_ACCOUNT_ID? | is-empty)
  or ($env.CLOUDFLARE_API_TOKEN? | is-empty)
) {
  error make { msg: "Cloudflare credentials are invalid; recreate .env with 'bun run setup'" }
}

let config = (
  read-wrangler-config ($env.FILE_PWD | path join "wrangler.jsonc")
)
let database_name = (
  $config.d1_databases
  | where binding == "SESSION_DB"
  | first
  | get database_name
)
let bucket_name = (
  $config.r2_buckets
  | where binding == "SESSION_SNAPSHOTS"
  | first
  | get bucket_name
)

run-step "Checking types" { bun run typecheck }
run-step "Running tests" { bun test }
ensure-d1-database $database_name
ensure-r2-bucket $bucket_name
run-step "Applying D1 migrations" {
  wrangler d1 migrations apply SESSION_DB --remote
}
run-step "Deploying Worker and sandbox" { wrangler deploy }
