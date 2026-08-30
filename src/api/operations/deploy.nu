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

def ensure-cache-lifecycle [name: string] {
  let result = (wrangler r2 bucket lifecycle list $name | complete)
  if $result.exit_code != 0 {
    fail-step "Checking cache lifecycle" $result
  }
  if not ($result.stdout | str contains "ai-sloth-cache-expiry") {
    run-step "Configuring cache lifecycle" {
      wrangler r2 bucket lifecycle add $name ai-sloth-cache-expiry backups/ --expire-days 8 --force
    }
  }
}

let repository_dir = ($env.FILE_PWD | path join "../../.." | path expand)
let env_file = ($env.FILE_PWD | path join "../.env")
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
  read-wrangler-config ($env.FILE_PWD | path join "../wrangler.jsonc")
)
let databases = $config.d1_databases
let buckets = $config.r2_buckets

run-step "Checking types" {
  bun $"--cwd=($repository_dir)" run typecheck
}
run-step "Running tests" {
  bun $"--cwd=($repository_dir)" run test
}
for database in $databases {
  ensure-d1-database $database.database_name
}
for bucket in $buckets {
  ensure-r2-bucket $bucket.bucket_name
}
let cache_bucket = (
  $buckets | where binding == "BACKUP_BUCKET" | first | get bucket_name
)
ensure-cache-lifecycle $cache_bucket
for database in $databases {
  run-step $"Applying migrations for '($database.binding)'" {
    wrangler d1 migrations apply $database.binding --remote
  }
}
run-step "Deploying Worker and sandbox" { wrangler deploy }
