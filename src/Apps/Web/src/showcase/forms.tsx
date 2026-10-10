import * as React from "react"
import type { DateRange } from "react-day-picker"
import {
  ArrowUpIcon,
  PaperclipIcon,
  MagnifyingGlassIcon,
} from "@phosphor-icons/react"

import { Calendar } from "@/components/ui/calendar"
import { Checkbox } from "@/components/ui/checkbox"
import {
  Combobox,
  ComboboxChip,
  ComboboxChips,
  ComboboxChipsInput,
  ComboboxContent,
  ComboboxEmpty,
  ComboboxItem,
  ComboboxList,
  ComboboxValue,
  useComboboxAnchor,
} from "@/components/ui/combobox"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldLegend,
  FieldSeparator,
  FieldSet,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupButton,
  InputGroupInput,
  InputGroupText,
  InputGroupTextarea,
} from "@/components/ui/input-group"
import {
  InputOTP,
  InputOTPGroup,
  InputOTPSeparator,
  InputOTPSlot,
} from "@/components/ui/input-otp"
import { Label } from "@/components/ui/label"
import { NativeSelect, NativeSelectOption } from "@/components/ui/native-select"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Slider } from "@/components/ui/slider"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import { Section, Specimen } from "./section"

const harnesses = [
  { label: "Claude Code", value: "claude-code" },
  { label: "Codex", value: "codex" },
  { label: "Copilot", value: "copilot" },
]

const repositories = [
  "acme/web",
  "acme/billing-service",
  "acme/infra",
  "acme/mobile",
  "acme/design-system",
]

export function Forms() {
  return (
    <Section id="forms" title="Forms">
      <Specimen name="Composer: input group with textarea" wide>
        <InputGroup>
          <InputGroupTextarea placeholder="Ask the agent to fix the flaky checkout test" />
          <InputGroupAddon align="block-end">
            <InputGroupButton
              variant="outline"
              size="icon-xs"
              aria-label="Attach files"
            >
              <PaperclipIcon />
            </InputGroupButton>
            <InputGroupText>Claude Code on your Claude plan</InputGroupText>
            <InputGroupButton
              variant="default"
              size="icon-xs"
              className="ml-auto"
              aria-label="Send"
            >
              <ArrowUpIcon />
            </InputGroupButton>
          </InputGroupAddon>
        </InputGroup>
      </Specimen>
      <Specimen name="Input, input group, label">
        <div className="flex w-full flex-col gap-2">
          <Label htmlFor="chat-title">Chat title</Label>
          <Input id="chat-title" defaultValue="Fix flaky checkout test" />
        </div>
        <InputGroup>
          <InputGroupAddon>
            <MagnifyingGlassIcon />
          </InputGroupAddon>
          <InputGroupInput placeholder="Search chats" />
        </InputGroup>
        <Input placeholder="Disabled" disabled />
      </Specimen>
      <Specimen name="Textarea">
        <Textarea
          className="w-full"
          defaultValue="Run the tests before you push. Keep commits small."
          aria-label="Workspace instructions"
        />
      </Specimen>
      <Specimen name="Select and native select">
        <Select items={harnesses} defaultValue="claude-code">
          <SelectTrigger className="w-44" aria-label="Harness">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectLabel>Harness</SelectLabel>
              {harnesses.map((harness) => (
                <SelectItem key={harness.value} value={harness.value}>
                  {harness.label}
                </SelectItem>
              ))}
            </SelectGroup>
          </SelectContent>
        </Select>
        <NativeSelect aria-label="Workspace" defaultValue="acme">
          <NativeSelectOption value="personal">Personal</NativeSelectOption>
          <NativeSelectOption value="acme">Acme</NativeSelectOption>
        </NativeSelect>
      </Specimen>
      <Specimen name="Combobox with chips">
        <Repositories />
      </Specimen>
      <Specimen name="Field set: git settings" wide>
        <FieldSet className="w-full max-w-lg">
          <FieldLegend>Git settings</FieldLegend>
          <FieldDescription>
            How your commits and branches look when an agent pushes for you.
          </FieldDescription>
          <FieldGroup>
            <Field>
              <FieldLabel htmlFor="author">Author</FieldLabel>
              <Input id="author" defaultValue="Ada Lovelace" />
            </Field>
            <Field data-invalid>
              <FieldLabel htmlFor="email">Email</FieldLabel>
              <Input id="email" type="email" defaultValue="ada@" aria-invalid />
              <FieldError>
                Enter an email address, like ada@acme.dev.
              </FieldError>
            </Field>
            <Field>
              <FieldLabel htmlFor="prefix">Branch prefix</FieldLabel>
              <Input id="prefix" defaultValue="ada/" />
              <FieldDescription>
                Agents push to branches that start with this.
              </FieldDescription>
            </Field>
            <FieldSeparator />
            <Field orientation="horizontal">
              <FieldContent>
                <FieldLabel htmlFor="co-author">
                  AiSloth as co-author
                </FieldLabel>
                <FieldDescription>
                  Adds a Co-authored-by line to each commit.
                </FieldDescription>
              </FieldContent>
              <Switch id="co-author" defaultChecked />
            </Field>
          </FieldGroup>
        </FieldSet>
      </Specimen>
      <Specimen name="Checkbox, switch, radio group">
        <div className="flex flex-col gap-4">
          <Field orientation="horizontal">
            <Checkbox id="open-pr" defaultChecked />
            <FieldLabel htmlFor="open-pr">Open a pull request</FieldLabel>
          </Field>
          <Field orientation="horizontal">
            <Switch id="notify" />
            <FieldLabel htmlFor="notify">Tell me when a turn ends</FieldLabel>
          </Field>
          <RadioGroup defaultValue="write" aria-label="Access level">
            {[
              ["read", "Read", "See chats and their nooks"],
              ["write", "Write", "Work in chats"],
              ["manage", "Manage", "Decide who else has access"],
            ].map(([value, label, description]) => (
              <Field orientation="horizontal" key={value}>
                <RadioGroupItem value={value} id={`access-${value}`} />
                <FieldContent>
                  <FieldLabel htmlFor={`access-${value}`}>{label}</FieldLabel>
                  <FieldDescription>{description}</FieldDescription>
                </FieldContent>
              </Field>
            ))}
          </RadioGroup>
        </div>
      </Specimen>
      <Specimen name="Slider">
        <div className="flex w-full flex-col gap-3">
          <Label>Sleep after 2 minutes unused</Label>
          <Slider defaultValue={[2]} min={1} max={30} />
          <Label className="mt-3">
            Warn when the disk is between 80% and 95%
          </Label>
          <Slider defaultValue={[80, 95]} min={50} max={100} />
        </div>
      </Specimen>
      <Specimen name="One-time code">
        <div className="flex flex-col gap-2">
          <Label>Link code from your other device</Label>
          <InputOTP maxLength={6}>
            <InputOTPGroup>
              <InputOTPSlot index={0} />
              <InputOTPSlot index={1} />
              <InputOTPSlot index={2} />
            </InputOTPGroup>
            <InputOTPSeparator />
            <InputOTPGroup>
              <InputOTPSlot index={3} />
              <InputOTPSlot index={4} />
              <InputOTPSlot index={5} />
            </InputOTPGroup>
          </InputOTP>
        </div>
      </Specimen>
      <Specimen name="Calendar">
        <UsageSince />
      </Specimen>
    </Section>
  )
}

function Repositories() {
  const anchor = useComboboxAnchor()

  return (
    <Combobox
      items={repositories}
      multiple
      defaultValue={["acme/web", "acme/billing-service"]}
    >
      <ComboboxChips ref={anchor} className="w-full max-w-sm">
        <ComboboxValue>
          {(values: string[]) =>
            values.map((value) => (
              <ComboboxChip key={value}>{value}</ComboboxChip>
            ))
          }
        </ComboboxValue>
        <ComboboxChipsInput placeholder="Add a repository" />
      </ComboboxChips>
      <ComboboxContent anchor={anchor}>
        <ComboboxEmpty>No repositories match.</ComboboxEmpty>
        <ComboboxList>
          {(repository: string) => (
            <ComboboxItem key={repository} value={repository}>
              {repository}
            </ComboboxItem>
          )}
        </ComboboxList>
      </ComboboxContent>
    </Combobox>
  )
}

function UsageSince() {
  const [range, setRange] = React.useState<DateRange | undefined>({
    from: new Date(2026, 9, 6),
    to: new Date(2026, 9, 12),
  })

  return (
    <Calendar
      mode="range"
      selected={range}
      onSelect={setRange}
      defaultMonth={new Date(2026, 9, 1)}
      className="rounded-xl shadow-raised"
    />
  )
}
