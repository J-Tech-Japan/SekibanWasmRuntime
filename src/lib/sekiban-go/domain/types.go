package domain

import (
	"encoding/json"
	"errors"
	"fmt"
	"strings"
)

// TagString formats a tag group and id as "group:id".
func TagString(group, id string) string {
	return fmt.Sprintf("%s:%s", group, id)
}

// EventOutput for commit.
type EventOutput struct {
	EventType string `json:"eventType"`
	Payload   string `json:"payload"`
}

// CommandOutput holds events, tags, and consistency info produced by a command.
type CommandOutput struct {
	Events           []EventOutput  `json:"events"`
	Tags             []string       `json:"tags"`
	ConsistencyTags  []string       `json:"consistencyTags"`
	ExpectedVersions map[string]int `json:"expectedVersions"`
}

// NewCommandOutput creates a single-event CommandOutput.
func NewCommandOutput(eventType string, payload any, tags []string, consistencyTags []string, expectedVersions map[string]int) (CommandOutput, error) {
	payloadJson, err := json.Marshal(payload)
	if err != nil {
		return CommandOutput{}, err
	}
	if expectedVersions == nil {
		expectedVersions = map[string]int{}
	}
	return CommandOutput{
		Events:           []EventOutput{{EventType: eventType, Payload: string(payloadJson)}},
		Tags:             tags,
		ConsistencyTags:  consistencyTags,
		ExpectedVersions: expectedVersions,
	}, nil
}

// NewMultiEventCommandOutput creates a CommandOutput with multiple events.
func NewMultiEventCommandOutput(events []EventOutput, tags []string, consistencyTags []string, expectedVersions map[string]int) CommandOutput {
	if expectedVersions == nil {
		expectedVersions = map[string]int{}
	}
	return CommandOutput{
		Events:           events,
		Tags:             tags,
		ConsistencyTags:  consistencyTags,
		ExpectedVersions: expectedVersions,
	}
}

// TagStateResponse from WasmServer.
type TagStateResponse struct {
	TagGroup             string  `json:"tagGroup"`
	TagId                string  `json:"tagId"`
	StateJson            string  `json:"stateJson"`
	Version              int     `json:"version"`
	LastSortableUniqueId *string `json:"lastSortableUniqueId"`
}

// Command is executable with a CommandContext.
type Command interface {
	CommandType() string
	Handle(ctx CommandContext) (*CommandOutput, error)
}

// CommandContext provides tag state access for commands.
type CommandContext interface {
	GetTagState(tagGroup, tagId string) (TagStateResponse, error)
}

// Standard errors.
var (
	ErrAlreadyExists = errors.New("already exists")
	ErrNotFound      = errors.New("not found")
	ErrDeleted       = errors.New("deleted")
	ErrValidation    = errors.New("validation error")
)

// IsEmptyJSON returns true if the string is empty, "{}", or "null".
func IsEmptyJSON(value string) bool {
	trimmed := strings.TrimSpace(value)
	return trimmed == "" || trimmed == "{}" || trimmed == "null"
}

// PagingQuery common pagination parameters.
type PagingQuery struct {
	PageNumber              *int    `json:"pageNumber"`
	PageSize                *int    `json:"pageSize"`
	WaitForSortableUniqueId *string `json:"waitForSortableUniqueId"`
}

// ApplyPaging applies offset/limit paging to a slice.
func ApplyPaging[T any](items []T, query PagingQuery) []T {
	if query.PageSize == nil || *query.PageSize <= 0 {
		return items
	}
	pageSize := *query.PageSize
	pageNumber := 1
	if query.PageNumber != nil && *query.PageNumber > 0 {
		pageNumber = *query.PageNumber
	}
	start := (pageNumber - 1) * pageSize
	if start >= len(items) {
		return []T{}
	}
	end := start + pageSize
	if end > len(items) {
		end = len(items)
	}
	return items[start:end]
}

// CountResult for count queries.
type CountResult struct {
	Count int `json:"count"`
}

// ContainsString checks if a string slice contains a value.
func ContainsString(list []string, value string) bool {
	for _, v := range list {
		if v == value {
			return true
		}
	}
	return false
}

// RemoveString removes a value from a string slice.
func RemoveString(list []string, value string) []string {
	result := make([]string, 0, len(list))
	for _, v := range list {
		if v != value {
			result = append(result, v)
		}
	}
	return result
}

// ParseJSON safely unmarshals JSON, returning zero value on error.
func ParseJSON[T any](jsonValue string) T {
	var result T
	if IsEmptyJSON(jsonValue) {
		return result
	}
	_ = json.Unmarshal([]byte(jsonValue), &result)
	return result
}
